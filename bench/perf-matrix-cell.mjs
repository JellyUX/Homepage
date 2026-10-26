// One cell of the load matrix (bench/perf-matrix.mjs runs it with JUX_URL, JUX_CONTAINER and JUX_BENCH_OUT set
// for the cell): a server already started on its profile, measured in three states and up to three browsers.
//   cold-warm : container restarted, warm-up finished (Homepage: its warm-up line; otherwise the same fixed
//               wait), then a cold page in a new browser context
//   cold-boot : container restarted, page as soon as /health answers (desktop only)
//   warm      : the same context reloads the home page; the first load warms the HTTP cache and is not counted
// Browsers: desktop (1920 x 1080), desktop-slow (same, CPU x4 and the low-end network), phone (390 x 844 at
// DPR 3, CPU x4 and the low-end network). Raws in the perf-bench-cycle format, so perf-bench-summarize.mjs
// reads them: perf-bench-browser-{std-desktop,lowend-desktop,lowend-mobile,boot-desktop}.json.
// Ported from the audit V5 mx-cell.mjs.
//   node bench/perf-matrix-cell.mjs --browsers desktop,phone --colds 3 --boot-colds 3 --warm-desktop 20
//        --warm-phone 10 --wait-s 100 [--banner]
import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { rawDir, dockerStats, sleep, waitHealthy, CONTAINER } from './perf-bench-common.mjs';
import { launch, ensureStorageState, storageStatePath, measurePage, validateTarget, CONTEXTS } from './perf-bench-browser.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';
import { waitForLog } from './bench-docker.mjs';

const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const browsers = arg('browsers', 'desktop,desktop-slow,phone').split(',');
const colds = Number(arg('colds', 3));
const bootColds = Number(arg('boot-colds', 3));
const warmCounts = { desktop: Number(arg('warm-desktop', 20)), 'desktop-slow': Number(arg('warm-desktop', 20)), phone: Number(arg('warm-phone', 10)) };
const waitS = Number(arg('wait-s', 100));
const banner = process.argv.includes('--banner');

export const BROWSERS = {
    desktop: { file: 'std-desktop', ctx: CONTEXTS.desktop, lowend: false, timeoutMs: 120000 },
    'desktop-slow': { file: 'lowend-desktop', ctx: CONTEXTS.desktop, lowend: true, timeoutMs: 300000 },
    phone: { file: 'lowend-mobile', ctx: CONTEXTS.mobile, lowend: true, timeoutMs: 300000 }
};

const log = (...a) => console.log(new Date().toISOString(), path.basename(rawDir), ...a);
const series = await startSeries({ label: 'perf-matrix-cell' });
const homepage = series.homepage !== null;

/** Restarts the container; with waitWarm, waits for the warm-up line (Homepage) or the fixed wait. */
async function restart(waitWarm) {
    const t0 = Date.now();
    execFileSync('docker', ['restart', '-t', '30', CONTAINER], { timeout: 180000 });
    const since = execFileSync('docker', ['inspect', CONTAINER, '--format={{.State.StartedAt}}'], { encoding: 'utf8' }).trim();
    await waitHealthy();
    const healthyMs = Date.now() - t0;
    let warmLine = null;
    if (waitWarm && homepage) {
        warmLine = await waitForLog(CONTAINER, since, /Built the home screen/, (waitS + 60) * 1000).then((w) => w.line.trim()).catch(() => null);
    } else if (waitWarm) {
        await sleep(Math.max(0, waitS * 1000 - (Date.now() - t0)));
    }
    return { startedAt: since, healthyMs, readyMs: Date.now() - t0, warmLine, memRest: dockerStats() };
}

const files = {};
const raw = (file) => (files[file] ||= { file, cycles: [] });
const save = () => { for (const f of Object.values(files)) { writeFileSync(path.join(rawDir, `perf-bench-browser-${f.file}.json`), JSON.stringify(f)); } };

const browser = await launch();
try {
    // The signed-in state (a token) stays out of the series folder, which is archived.
    const scratch = process.env.PERF_SCRATCH || rawDir;
    mkdirSync(scratch, { recursive: true });
    const state = await ensureStorageState(browser, storageStatePath(scratch));
    const context = (b) => browser.newContext({ storageState: state, locale: 'en-US', ...BROWSERS[b].ctx });
    for (const b of browsers) {
        const probe = await context(b);
        try {
            writeFileSync(path.join(rawDir, `selectors-${b}.json`), JSON.stringify(await validateTarget(probe, { banner, timeoutMs: BROWSERS[b].timeoutMs }), null, 1));
        } finally { await probe.close(); }
    }
    const cold = async (b, file, i, st) => {
        const ctx = await context(b);
        try {
            const r = await measurePage(ctx, { lowend: BROWSERS[b].lowend, timeoutMs: BROWSERS[b].timeoutMs, doTabs: false });
            raw(file).cycles.push({ cycle: i, startup: st, memRest: st.memRest, cold: r, warm: [] });
            log(b, file, i, 'first', Math.round(r.home.firstSectionMs));
        } catch (e) {
            raw(file).cycles.push({ cycle: i, startup: st, memRest: st.memRest, error: String(e.message || e).slice(0, 300) });
            log(b, file, i, 'ERROR', String(e.message || e).slice(0, 120));
        } finally { await ctx.close(); save(); }
    };
    for (let i = 1; i <= colds; i++) {
        for (const b of browsers) { await cold(b, BROWSERS[b].file, i, await restart(true)); }
    }
    if (browsers.includes('desktop')) {
        for (let i = 1; i <= bootColds; i++) { await cold('desktop', 'boot-desktop', i, await restart(false)); }
    }
    for (const b of browsers) {
        const ctx = await context(b);
        const warm = [];
        try {
            await measurePage(ctx, { lowend: BROWSERS[b].lowend, timeoutMs: BROWSERS[b].timeoutMs, doBack: false, doTabs: false });
            for (let i = 1; i <= warmCounts[b]; i++) {
                try {
                    warm.push(await measurePage(ctx, { lowend: BROWSERS[b].lowend, timeoutMs: BROWSERS[b].timeoutMs, doTabs: false }));
                } catch (e) { warm.push({ error: String(e.message || e).slice(0, 300) }); }
            }
        } finally { await ctx.close(); }
        raw(BROWSERS[b].file).cycles.push({ cycle: 'warm', memRest: dockerStats(), warm });
        log(b, 'warm', warm.length, 'pages');
        save();
    }
} finally {
    await browser.close();
}
await endSeries(series);
log('done');
