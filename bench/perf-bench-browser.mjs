// Browser-side measurements of the JellyUX home page and tabs (Playwright Chromium + CDP).
// Usage as a module (see perf-bench-cycle.mjs) or standalone for one exploratory run:
//   node bench/perf-bench-browser.mjs [--lowend] [--mobile]
import { chromium } from '@playwright/test';
import { readFileSync, existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { serverUrl, readCredentials, sleep, rawDir } from './perf-bench-common.mjs';

export const VIEWPORTS = { desktop: { width: 1920, height: 1080 }, mobile: { width: 390, height: 844 } };
// Low-end profile network: 150 ms latency, 1.6 Mbit/s down, 750 kbit/s up, both x0.9 as DevTools does.
export const LOWEND_NETWORK = { offline: false, latency: 150, downloadThroughput: Math.round(1.6e6 * 0.9 / 8), uploadThroughput: Math.round(750e3 * 0.9 / 8) };
export const LOWEND_CPU_RATE = 4;
export const TABS = [
    { index: 2, id: 'jux-tab-watchlist', name: 'watchlist' },
    { index: 3, id: 'jux-tab-progress', name: 'progress' },
    { index: 4, id: 'jux-tab-history', name: 'history' },
    { index: 5, id: 'jux-tab-statistics', name: 'statistics' }
];

// Runs before every document: long tasks, long animation frames (script attribution), and a
// per-frame poll recording when each JellyUX section first holds a visible card.
const INIT = `(() => {
  const P = window.__juxPerf = { longtasks: [], loaf: [], sections: [], firstSection: null, lastChange: 0, t0: 0 };
  try { new PerformanceObserver((l) => l.getEntries().forEach((e) => P.longtasks.push({ start: e.startTime, dur: e.duration, attr: (e.attribution || []).map((a) => a.containerSrc || a.containerName || a.name) }))).observe({ type: 'longtask', buffered: true }); } catch (e) {}
  try { new PerformanceObserver((l) => l.getEntries().forEach((e) => P.loaf.push({ start: e.startTime, dur: e.duration, blocking: e.blockingDuration, scripts: (e.scripts || []).map((s) => ({ src: s.sourceURL, fn: s.sourceFunctionName, dur: s.duration, inv: s.invoker, charPos: s.sourceCharPosition })) }))).observe({ type: 'long-animation-frame', buffered: true }); } catch (e) {}
  const seen = new WeakMap();
  P.reset = () => { P.sections = []; P.firstSection = null; P.t0 = performance.now(); P.lastChange = P.t0; P.gen = (P.gen || 0) + 1; };
  const tick = () => {
    const now = performance.now();
    const list = document.querySelectorAll('.page:not(.hide) .jux-widget-section');
    for (const s of list) {
      let rec = seen.get(s);
      if (!rec || rec.gen !== (P.gen || 0)) {
        rec = { gen: P.gen || 0, cls: ([...s.classList].find((c) => c.startsWith('jux-widget-jux-')) || '?').replace('jux-widget-', ''), created: now - P.t0, card: null, visible: null };
        seen.set(s, rec); P.sections.push(rec); P.lastChange = now;
      }
      if (rec.card === null) {
        const card = s.querySelector('.card:not(.jux-skeleton-card)');
        if (card) { rec.card = now - P.t0; P.lastChange = now; }
      }
      if (rec.visible === null && rec.card !== null) {
        const card = s.querySelector('.card:not(.jux-skeleton-card)');
        const r = card.getBoundingClientRect();
        if (r.height > 0 && r.width > 0 && r.top < innerHeight && r.bottom > 0) {
          rec.visible = now - P.t0;
          if (P.firstSection === null) { P.firstSection = rec.visible; }
        }
      }
    }
    requestAnimationFrame(tick);
  };
  requestAnimationFrame(tick);
})();`;

// One signed-in state per server port, so a token is never replayed against another server.
export function storageStatePath(dir) { return path.join(dir, `perf-bench-storage-test2-${new URL(serverUrl).port || '80'}.json`); }

/** Signs test2 in once through the login form and saves the storage state (token) outside the repo. */
export async function ensureStorageState(browser, file) {
    if (existsSync(file)) { return file; }
    const { user } = readCredentials();
    const context = await browser.newContext({ viewport: VIEWPORTS.desktop, locale: 'en-US' });
    const page = await context.newPage();
    page.on('crash', () => console.error('PAGE CRASH'));
    page.on('close', () => { if (process.env.PERF_DEBUG) { console.error(new Date().toISOString(), 'page closed'); } });
    page.on('console', (m) => { if (process.env.PERF_DEBUG && m.type() === 'error') { console.error('console', m.text().slice(0, 200)); } });
    await page.goto(`${serverUrl}/web/`);
    const name = page.locator('#txtManualName:visible');
    const manual = page.locator('.btnManual:visible');
    const deadline = Date.now() + 60000;
    while (!(await name.isVisible())) {
        if (Date.now() > deadline) { throw new Error('login form never visible'); }
        if (await manual.isVisible()) { await manual.click({ timeout: 2000 }).catch(() => {}); }
        await page.waitForTimeout(500);
    }
    await name.fill(user.name);
    await page.locator('#txtManualPassword:visible').fill(user.password);
    await page.locator('.manualLoginForm:visible .button-submit').click();
    await page.waitForFunction(() => location.hash.startsWith('#/home'), null, { timeout: 60000 });
    await page.waitForTimeout(3000);
    await context.storageState({ path: file });
    await context.close();
    return file;
}

function networkTracker(cdp) {
    const reqs = new Map();
    const state = { phase: 'init', lastRequest: Date.now(), inflight: 0 };
    cdp.on('Network.requestWillBeSent', (e) => {
        if (e.request.url.startsWith('data:')) { return; }
        if (!reqs.has(e.requestId)) { state.inflight++; }
        reqs.set(e.requestId, { ts: e.timestamp, url: e.request.url, method: e.request.method, type: e.type, phase: state.phase, t: Date.now(), bytes: 0, status: null, fromCache: false, done: false });
        state.lastRequest = Date.now();
    });
    cdp.on('Network.requestServedFromCache', (e) => { const r = reqs.get(e.requestId); if (r) { r.fromCache = true; } });
    cdp.on('Network.responseReceived', (e) => {
        const r = reqs.get(e.requestId);
        if (r) { r.status = e.response.status; if (e.response.fromDiskCache || e.response.fromServiceWorker || e.response.fromPrefetchCache) { r.fromCache = true; } }
    });
    const finish = (e, failed) => {
        const r = reqs.get(e.requestId);
        if (r && !r.done) { r.done = true; r.tsEnd = e.timestamp; r.failed = failed; r.bytes = e.encodedDataLength || 0; state.inflight--; state.lastRequest = Date.now(); }
    };
    cdp.on('Network.loadingFinished', (e) => finish(e, false));
    cdp.on('Network.loadingFailed', (e) => finish(e, true));
    return { reqs, state };
}

function summarize(reqs, phase) {
    const list = [...reqs.values()].filter((r) => r.phase === phase);
    const jux = list.filter((r) => r.url.includes('/JuxHomepage/'));
    const count = (arr) => {
        const m = new Map();
        for (const r of arr) { const key = `${r.method} ${r.url.replace(serverUrl, '')}`; m.set(key, (m.get(key) || 0) + 1); }
        return [...m.entries()].filter(([, n]) => n > 1).map(([url, n]) => ({ url, n }));
    };
    return {
        requests: list.length,
        networkRequests: list.filter((r) => !r.fromCache).length,
        bytes: list.reduce((a, r) => a + r.bytes, 0),
        juxRequests: jux.length,
        juxBytes: jux.reduce((a, r) => a + r.bytes, 0),
        duplicates: count(list),
        list: list.map((r) => ({ t: list.length ? Math.round((r.ts - list[0].ts) * 1000) : null, dur: r.tsEnd ? Math.round((r.tsEnd - r.ts) * 1000) : null, m: r.method, url: r.url.replace(serverUrl, ''), status: r.status, bytes: r.bytes, cache: r.fromCache, type: r.type, failed: r.failed || false }))
    };
}

async function waitQuiet(page, net, { quietMs = 3000, maxMs = 90000 } = {}) {
    const t0 = Date.now();
    while (Date.now() - t0 < maxMs) {
        const lastChange = await page.evaluate(() => performance.now() - window.__juxPerf.lastChange);
        const now = Date.now();
        const pending = [...net.reqs.values()].filter((r) => !r.done && now - r.t < 10000 && r.type !== 'WebSocket' && r.type !== 'EventSource').length;
        if (lastChange > quietMs && now - net.state.lastRequest > quietMs && pending === 0) { return true; }
        await sleep(250);
    }
    return false;
}

/** Opens the home page in a new page of the given context and measures it, then SPA return and tabs. */
export async function measurePage(context, { lowend = false, doBack = true, doTabs = true, timeoutMs = 120000, afterHome = null } = {}) {
    const page = await context.newPage();
    await page.addInitScript(INIT);
    const cdp = await context.newCDPSession(page);
    await cdp.send('Performance.enable');
    await cdp.send('Network.enable');
    if (lowend) {
        await cdp.send('Emulation.setCPUThrottlingRate', { rate: LOWEND_CPU_RATE });
        await cdp.send('Network.emulateNetworkConditions', LOWEND_NETWORK);
    }
    const net = networkTracker(cdp);
    const out = { home: {}, back: null, tabs: {} };
    net.state.phase = 'home';
    await page.goto(`${serverUrl}/web/#/home`, { waitUntil: 'commit', timeout: timeoutMs });
    await page.waitForFunction(() => window.__juxPerf && window.__juxPerf.firstSection !== null, null, { timeout: timeoutMs, polling: 100 });
    out.home.quiet = await waitQuiet(page, net, { maxMs: timeoutMs });
    if (process.env.PERF_DEBUG) { console.error(new Date().toISOString(), 'home quiet', out.home.quiet); }
    const perf = await page.evaluate(() => {
        const P = window.__juxPerf;
        const nav = performance.getEntriesByType('navigation')[0];
        return {
            firstSectionMs: P.firstSection,
            sections: P.sections.map((s) => ({ cls: s.cls, created: s.created, card: s.card, visible: s.visible })),
            longtasks: P.longtasks, loaf: P.loaf.filter((f) => f.dur > 50),
            domContentLoaded: nav ? nav.domContentLoadedEventEnd : null, load: nav ? nav.loadEventEnd : null,
            homeVisibleSections: document.querySelectorAll('.page:not(.hide) .jux-widget-section:not(.hide)').length
        };
    });
    const withCard = perf.sections.filter((s) => s.card !== null);
    out.home.firstSectionMs = perf.firstSectionMs;
    out.home.allSectionsMs = withCard.length ? Math.max(...withCard.map((s) => s.card)) : null;
    out.home.sectionsWithCards = withCard.length;
    out.home.sectionsTotal = perf.sections.length;
    out.home.sections = perf.sections;
    out.home.domContentLoaded = perf.domContentLoaded;
    out.home.longtasks = perf.longtasks;
    out.home.longtaskCount = perf.longtasks.filter((t) => t.dur > 50).length;
    out.home.longtaskTotalMs = perf.longtasks.filter((t) => t.dur > 50).reduce((a, t) => a + t.dur, 0);
    out.home.longtaskMaxMs = perf.longtasks.reduce((a, t) => Math.max(a, t.dur), 0);
    out.home.loaf = perf.loaf;
    const metrics = Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((m) => [m.name, m.value]));
    out.home.scriptDurationMs = metrics.ScriptDuration * 1000;
    out.home.taskDurationMs = metrics.TaskDuration * 1000;
    out.home.layoutDurationMs = metrics.LayoutDuration * 1000;
    out.home.jsHeapUsedMiB = metrics.JSHeapUsedSize / 1048576;
    out.home.net = summarize(net.reqs, 'home');

    if (afterHome) { out.afterHome = await afterHome(); }

    if (doBack) {
        const target = await page.evaluate(() => {
            const card = document.querySelector('.page:not(.hide) .jux-widget-jux-native-recently-added-movies .card[data-id]')
                || document.querySelector('.page:not(.hide) .jux-widget-section .card[data-id]');
            return card ? card.getAttribute('data-id') : null;
        });
        if (target) {
            await page.evaluate((id) => { location.hash = `#/details?id=${id}`; }, target);
            await page.waitForFunction(() => { const d = document.querySelector('.itemDetailPage:not(.hide)'); return d && d.querySelector('.itemName, h1, h3'); }, null, { timeout: timeoutMs });
            await waitQuiet(page, net, { maxMs: timeoutMs });
            net.state.phase = 'back';
            const tSpa = await page.evaluate(() => { window.__juxPerf.reset(); window.__juxOldFirst = document.querySelector('.homeSectionsContainer .jux-widget-section'); history.back(); return performance.now(); });
            await page.waitForFunction(() => window.__juxPerf.firstSection !== null && location.hash.startsWith('#/home'), null, { timeout: timeoutMs, polling: 50 });
            await waitQuiet(page, net, { maxMs: timeoutMs });
            const b = await page.evaluate(() => {
                const P = window.__juxPerf;
                const first = document.querySelector('.page:not(.hide) .jux-widget-section');
                return { firstSectionMs: P.firstSection, sections: P.sections.length, rebuilt: first !== window.__juxOldFirst, withCards: P.sections.filter((s) => s.card !== null).map((s) => s.card) };
            });
            out.back = { detailItemId: target, tStart: tSpa, firstSectionMs: b.firstSectionMs, allSectionsMs: b.withCards.length ? Math.max(...b.withCards) : null, rebuilt: b.rebuilt, sections: b.sections, net: summarize(net.reqs, 'back') };
        }
    }

    if (doTabs) {
        for (const tab of TABS) {
            net.state.phase = `tab-${tab.name}`;
            if (process.env.PERF_DEBUG) { console.error(new Date().toISOString(), 'tab', tab.name); }
            const res = await page.evaluate(({ index, id, timeout }) => new Promise((resolve) => {
                const t0 = performance.now();
                const btn = document.querySelector(`.emby-tab-button[data-index="${index}"]`);
                if (!btn) { resolve({ error: 'no tab button' }); return; }
                btn.click();
                let firstCard = null;
                const check = () => {
                    const now = performance.now() - t0;
                    const pane = document.getElementById(id);
                    const visible = pane && pane.offsetParent !== null && pane.getBoundingClientRect().height > 0;
                    if (visible && firstCard === null && pane.querySelector('.card, .jux-stat-card')) { firstCard = now; }
                    const loaded = visible && !pane.querySelector('[data-jux-loading]') && !!pane.querySelector('.card, .jux-empty-state, .jux-stat-card');
                    if (loaded) { resolve({ renderedMs: now, firstCardMs: firstCard, cards: pane.querySelectorAll('.card').length, empty: !!pane.querySelector('.jux-empty-state'), statCards: pane.querySelectorAll('.jux-stat-card').length }); return; }
                    if (now > timeout) { resolve({ error: 'timeout', renderedMs: null }); return; }
                    requestAnimationFrame(check);
                };
                requestAnimationFrame(check);
            }), { index: tab.index, id: tab.id, timeout: timeoutMs });
            await waitQuiet(page, net, { quietMs: 2000, maxMs: timeoutMs });
            out.tabs[tab.name] = { ...res, net: summarize(net.reqs, `tab-${tab.name}`) };
        }
    }
    await cdp.detach().catch(() => {});
    await page.close();
    return out;
}

export async function launch() {
    return chromium.launch({ headless: true, timeout: 60000 });
}

if (process.argv[1] && process.argv[1].endsWith('perf-bench-browser.mjs')) {
    const scratch = process.env.PERF_SCRATCH || rawDir;
    const browser = await launch();
    try {
        const state = await ensureStorageState(browser, storageStatePath(scratch));
        const vp = process.argv.includes('--mobile') ? VIEWPORTS.mobile : VIEWPORTS.desktop;
        const context = await browser.newContext({ storageState: state, viewport: vp, locale: 'en-US' });
        const r = await measurePage(context, { lowend: process.argv.includes('--lowend') });
        const file = path.join(scratch, 'explore.json');
        writeFileSync(file, JSON.stringify(r, null, 1));
        console.log(JSON.stringify({ home: { ...r.home, sections: r.home.sections, longtasks: undefined, loaf: r.home.loaf.length, net: { ...r.home.net, list: r.home.net.list.length } }, back: r.back && { ...r.back, net: { ...r.back.net, list: r.back.net.list.filter((x) => !x.cache).map((x) => x.url).slice(0, 40) } }, tabs: Object.fromEntries(Object.entries(r.tabs).map(([k, v]) => [k, { ...v, net: { ...v.net, list: v.net.list.map((x) => x.url) } }])) }, null, 1));
        await context.close();
    } finally {
        await browser.close();
    }
}
