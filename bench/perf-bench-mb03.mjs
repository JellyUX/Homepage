// MB-03 replay (DR-008): does the section page cache survive a playback and a library scan?
// Run it once per DLL (A: the release before the change, B: the build under test) on a disposable bench with
// JellyUX Homepage installed, never on 8096 / 8097. Cached sections: every layout row the client does not read
// natively (jux-homepage.js NATIVE_ROUTES: continue-watching, next-up, recently-added-*), read with the query the
// client sends (additionalData, sectionKey, startIndex 0, limit MaxItems).
//
// playback: the bench user (second login of identifiants.txt) plays a movie for --minutes (POST /Sessions/Playing,
//   then /Sessions/Playing/Progress every --progress-every s, then /Sessions/Playing/Stopped below 50 % of the
//   runtime, so the movie is never marked played); its user data is restored after. Every cached section is read
//   every --read-every s. Each section is calibrated first (one warming read, 5 immediate re-reads = hit samples);
//   a read is a hit when <= max(3 x median of the hit samples, median + 15 ms). Criterion: overall hit rate >= 50 %.
// scan: --count placeholder movies (a few bytes each) are written to --scan-dir-host, a host folder the operator
//   mounted into the bench inside a movie library path; POST /Library/Refresh (admin) starts the "RefreshLibrary"
//   task, and while it runs 3 readers (3 sessions of the bench user, the scripts know no other bench user) read
//   every cached section every 5 s. Warm baseline first: 20 reads per section. A read is cold when > 2 x the warm
//   p95 of its section. The placeholders are deleted and the library refreshed again at the end (bench restored).
//
//   node bench/perf-bench-mb03.mjs --url http://localhost:8112 --container jux-bench-mb03 --part playback --minutes 10
//   node bench/perf-bench-mb03.mjs --url http://localhost:8112 --container jux-bench-mb03 --part scan --scan-dir-host D:/bench/mb03-scan --count 500
//   node bench/perf-bench-mb03.mjs --url http://localhost:8112 --container jux-bench-mb03 --part scan --scan-dir-host D:/bench/mb03-scan --cleanup
// Raw results: <JUX_BENCH_OUT>/perf-bench-mb03-<part>-<label>.json (fingerprint.json next to it); summary on stdout.
import { existsSync, mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import path from 'node:path';

const HELP = `Usage: node bench/perf-bench-mb03.mjs --url <disposable bench> [options]
  --url <url>              server to measure (or JUX_URL); 8096 and 8097 are refused
  --container <name>       its Docker container (or JUX_CONTAINER), read only: fingerprint, DLL md5, log tail
  --part playback|scan|both  default playback
  --label <text>           suffix of the raw file name (default: std)
  playback: --minutes 10 --progress-every 10 --read-every 30 [--item-id <movie id>]
  scan:     --scan-dir-host <host folder mounted in a movie library> --count 500 --read-step 5 --readers 3
            --scan-timeout-min 60 [--cleanup: only delete leftover placeholders, refresh, exit]`;
const argv = process.argv.slice(2);
if (argv.includes('--help') || argv.includes('-h')) { console.log(HELP); process.exit(0); }
const arg = (n, d) => { const i = argv.indexOf(`--${n}`); return i >= 0 && i + 1 < argv.length ? argv[i + 1] : d; };
const url = (arg('url', process.env.JUX_URL || '') || '').replace(/\/+$/, '');
if (!url) { console.error('--url is required (a disposable bench)\n' + HELP); process.exit(2); }
if (/:(8096|8097)$/.test(url)) { console.error('disposable benches only: 8096 and 8097 are refused'); process.exit(2); }
process.env.JUX_URL = url;
const container = arg('container', null);
if (container) { process.env.JUX_CONTAINER = container; }

const part = arg('part', 'playback');
if (!['playback', 'scan', 'both'].includes(part)) { console.error(`unknown --part ${part}`); process.exit(2); }
const label = arg('label', 'std');
const opt = {
    minutes: Number(arg('minutes', 10)), progressEvery: Number(arg('progress-every', 10)), readEvery: Number(arg('read-every', 30)),
    itemId: arg('item-id', null), scanDir: arg('scan-dir-host', null), count: Number(arg('count', 500)), readStep: Number(arg('read-step', 5)),
    readers: Number(arg('readers', 3)), scanTimeoutMin: Number(arg('scan-timeout-min', 60)), cleanupOnly: argv.includes('--cleanup')
};
if ((part === 'scan' || part === 'both') && !opt.scanDir) { console.error('--scan-dir-host is required for the scan part'); process.exit(2); }

// Imported after JUX_URL / JUX_CONTAINER are set: perf-bench-common reads them at load time.
const { readCredentials, rawDir, sleep, stats, tail, CONTAINER } = await import('./perf-bench-common.mjs');
const { startSeries, endSeries } = await import('./bench-fingerprint.mjs');
const { dockerOrNull } = await import('./bench-docker.mjs');

const PLACEHOLDER = /^JuxBench Movie \d{4} \(\d{4}\)$/;
const placeholderName = (i) => { const n = String(i).padStart(4, '0'); return `JuxBench Movie ${n} (${2000 + (i % 20)})`; };
const leftovers = () => (existsSync(opt.scanDir) ? readdirSync(opt.scanDir).filter((n) => PLACEHOLDER.test(n)) : []);
const NATIVE_READ = new Set(['jux.native.continue-watching', 'jux.native.next-up', 'jux.native.recently-added-movies', 'jux.native.recently-added-shows', 'jux.native.recently-added-episodes']);
const TICKS_PER_S = 10000000;
const round1 = (x) => (x === null || x === undefined ? null : Math.round(x * 10) / 10);
const fmt = (x) => (x === null || x === undefined ? 'n/a' : x.toFixed(0));

// One session per call: Jellyfin revokes the previous token of a DeviceId, so each reader gets its own.
async function signIn(which) {
    const auth = `MediaBrowser Client="jux-perf-bench", Device="mb03", DeviceId="jux-mb03-${randomUUID()}", Version="1.0.0"`;
    const c = readCredentials()[which];
    const res = await fetch(`${url}/Users/AuthenticateByName`, { method: 'POST', headers: { Authorization: auth, 'Content-Type': 'application/json' }, body: JSON.stringify({ Username: c.name, Pw: c.password }) });
    if (!res.ok) { throw new Error(`auth failed ${res.status}`); }
    const j = await res.json();
    return { base: url, userId: j.User.Id, header: `${auth}, Token="${j.AccessToken}"` };
}

async function call(s, route, { method = 'GET', body } = {}) {
    const res = await fetch(`${url}${route}`, { method, headers: { Authorization: s.header, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await res.text();
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
    if (res.status === 401) { throw new Error(`401 on ${route}: token revoked`); }
    return { status: res.status, body: json };
}

/** Duration of one section read, body included (ms). */
async function timedRead(s, sec) {
    const t0 = performance.now();
    const res = await fetch(`${url}${sec.route}`, { headers: { Authorization: s.header } });
    await res.arrayBuffer();
    const ms = performance.now() - t0;
    if (res.status === 401) { throw new Error('401 on a section read: token revoked'); }
    return { status: res.status, ms };
}

/** The cached rows of the user's layout (every page), with the query string the client sends. */
async function cachedSections(s) {
    const rows = [];
    for (let page = 0; page < 20; page++) {
        const r = await call(s, `/JuxHomepage/Sections?userId=${s.userId}&page=${page}&lang=en-us`);
        if (r.status !== 200) { throw new Error(`Sections page ${page}: HTTP ${r.status}`); }
        rows.push(...(r.body.Sections || []));
        if (!r.body.HasMore) { break; }
    }
    return rows.filter((row) => !NATIVE_READ.has(row.WidgetType)).map((row) => {
        const q = new URLSearchParams({ userId: s.userId });
        if (row.AdditionalData) { q.set('additionalData', row.AdditionalData); }
        if (row.SectionKey) { q.set('sectionKey', row.SectionKey); }
        q.set('startIndex', '0');
        q.set('limit', String(row.MaxItems || 20));
        return { key: row.SectionKey || `${row.WidgetType}|${row.AdditionalData || ''}`, type: row.WidgetType, additionalData: row.AdditionalData || null, route: `/JuxHomepage/Section/${row.WidgetType}?${q}` };
    });
}

/** Log lines of the bench container since `sinceIso` that name JellyUX, the library or a scan (null without a container). */
function logTail(sinceIso, max = 200) {
    const out = dockerOrNull(['logs', '--since', sinceIso, CONTAINER]);
    if (out === null) { return null; }
    const lines = out.split(/\r?\n/).filter((l) => /JellyUX|JuxHomepage|Jux|RefreshLibrary|Library|scan/i.test(l));
    return { matched: lines.length, tail: lines.slice(-max) };
}

const cleanups = [];
let interrupted = false;
process.on('SIGINT', async () => {
    if (interrupted) { process.exit(130); }
    interrupted = true;
    console.error('interrupted: restoring the bench state');
    for (const f of cleanups.reverse()) { try { await f(); } catch (e) { console.error('cleanup failed:', e.message); } }
    process.exit(130);
});

// ---------- playback ----------

async function pickMovie(s) {
    if (opt.itemId) {
        const r = await call(s, `/Items/${opt.itemId}?userId=${s.userId}`);
        if (r.status !== 200) { throw new Error(`--item-id ${opt.itemId}: HTTP ${r.status}`); }
        return r.body;
    }
    const need = opt.minutes * 60 * TICKS_PER_S * 2;
    const r = await call(s, `/Items?userId=${s.userId}&IncludeItemTypes=Movie&Recursive=true&IsPlayed=false&SortBy=SortName&SortOrder=Ascending&Fields=UserData&Limit=2000`);
    const m = (r.body.Items || []).find((i) => (i.RunTimeTicks || 0) >= need && i.UserData && !i.UserData.Played && !i.UserData.PlaybackPositionTicks && !i.UserData.IsFavorite && !i.UserData.Likes);
    if (!m) { throw new Error(`no unplayed, untouched movie longer than ${opt.minutes * 2} min: pass --item-id`); }
    return m;
}

async function runPlayback(series) {
    const reader = await signIn('user');
    const player = await signIn('user');
    const sections = await cachedSections(reader);
    if (sections.length === 0) { throw new Error('no cached section in the layout'); }
    const ttl = series.homepage ? series.homepage.sessionTtlMinutes : null;
    if (ttl !== null && opt.minutes + 2 >= ttl) { console.warn(`WARNING: --minutes ${opt.minutes} close to SessionTtlMinutes ${ttl}: pages expire on their own during the playback`); }

    // Calibration: one warming read, then 5 immediate re-reads per section.
    const calibration = {};
    for (const sec of sections) {
        await timedRead(reader, sec);
        const hits = [];
        for (let i = 0; i < 5; i++) { hits.push((await timedRead(reader, sec)).ms); }
        const h = stats(hits);
        calibration[sec.key] = { hitSamples: hits.map(round1), median: round1(h.median), threshold: round1(Math.max(3 * h.median, h.median + 15)) };
    }

    const movie = await pickMovie(player);
    const ud0 = movie.UserData || {};
    const original = { PlaybackPositionTicks: ud0.PlaybackPositionTicks || 0, Played: !!ud0.Played, PlayCount: ud0.PlayCount || 0, LastPlayedDate: ud0.LastPlayedDate || null };
    const playSessionId = randomUUID().replace(/-/g, '');
    const info = (ticks, extra = {}) => ({ ItemId: movie.Id, PlaySessionId: playSessionId, PositionTicks: ticks, CanSeek: true, IsPaused: false, IsMuted: false, PlayMethod: 'DirectPlay', ...extra });
    let stopped = false;
    const t0 = Date.now();
    const ticksNow = () => Math.floor(((Date.now() - t0) / 1000) * TICKS_PER_S);
    const stop = async () => {
        if (stopped) { return; }
        stopped = true;
        const r = await call(player, '/Sessions/Playing/Stopped', { method: 'POST', body: { ItemId: movie.Id, PlaySessionId: playSessionId, PositionTicks: ticksNow() } });
        if (r.status >= 300) { console.warn(`Stopped: HTTP ${r.status}`); }
    };
    const restore = async () => {
        const r = await call(player, `/UserItems/${movie.Id}/UserData?userId=${player.userId}`, { method: 'POST', body: original });
        if (r.status >= 300) { console.warn(`user data restore: HTTP ${r.status}`); }
    };
    cleanups.push(restore, stop);

    const started = await call(player, '/Sessions/Playing', { method: 'POST', body: info(0) });
    if (started.status >= 300) { throw new Error(`/Sessions/Playing: HTTP ${started.status}`); }
    const endAt = t0 + opt.minutes * 60000;
    const reads = [];
    const progress = [];
    const progressLoop = (async () => {
        for (let k = 1; t0 + k * opt.progressEvery * 1000 < endAt; k++) {
            const target = t0 + k * opt.progressEvery * 1000;
            await sleep(Math.max(0, target - Date.now()));
            const r = await call(player, '/Sessions/Playing/Progress', { method: 'POST', body: info(ticksNow(), { EventName: 'TimeUpdate' }) });
            progress.push({ tS: round1((Date.now() - t0) / 1000), status: r.status });
        }
    })();
    const pass = async (phase) => {
        for (const sec of sections) {
            const x = await timedRead(reader, sec);
            reads.push({ phase, tS: round1((Date.now() - t0) / 1000), key: sec.key, status: x.status, ms: round1(x.ms), hit: x.ms <= calibration[sec.key].threshold });
        }
    };
    const readLoop = (async () => {
        for (let k = 1; t0 + k * opt.readEvery * 1000 < endAt; k++) {
            await sleep(Math.max(0, t0 + k * opt.readEvery * 1000 - Date.now()));
            await pass('playback');
        }
    })();
    await Promise.all([progressLoop, readLoop]);
    await sleep(Math.max(0, endAt - Date.now()));
    await stop();
    // After the stop: B serves deferred pages once (stale) and re-reads them in the background.
    await sleep(2000);
    await pass('after-stop');
    await sleep(5000);
    await pass('after-stop+5s');
    await restore();
    const after = (await call(player, `/Items/${movie.Id}?userId=${player.userId}`)).body.UserData || {};
    cleanups.length = 0;
    await call(reader, '/Sessions/Logout', { method: 'POST' }).catch(() => {});
    await call(player, '/Sessions/Logout', { method: 'POST' }).catch(() => {});

    const summarize = (rs) => { const s = stats(rs.map((r) => r.ms)); return { n: rs.length, hitRatePct: rs.length ? round1((100 * rs.filter((r) => r.hit).length) / rs.length) : null, median: round1(s.median), tail: tail(s, (x) => x.toFixed(0)), p95: round1(s.p95), max: round1(s.max) }; };
    const during = reads.filter((r) => r.phase === 'playback');
    const perSection = Object.fromEntries(sections.map((sec) => [sec.key, { type: sec.type, threshold: calibration[sec.key].threshold, ...summarize(during.filter((r) => r.key === sec.key)) }]));
    const overall = summarize(during);
    return {
        options: { minutes: opt.minutes, progressEvery: opt.progressEvery, readEvery: opt.readEvery },
        movie: { id: movie.Id, name: movie.Name, runTimeTicks: movie.RunTimeTicks, stopPct: round1((100 * opt.minutes * 60 * TICKS_PER_S) / movie.RunTimeTicks) },
        userData: { before: original, afterRestore: { PlaybackPositionTicks: after.PlaybackPositionTicks, Played: after.Played, PlayCount: after.PlayCount, LastPlayedDate: after.LastPlayedDate } },
        sections: sections.map(({ key, type, additionalData }) => ({ key, type, additionalData })),
        calibration, progressReports: progress, reads, perSection, overall,
        afterStop: { now: summarize(reads.filter((r) => r.phase === 'after-stop')), plus5s: summarize(reads.filter((r) => r.phase === 'after-stop+5s')) },
        criterion: { rule: 'overall hit rate during playback >= 50 %', pass: overall.hitRatePct !== null && overall.hitRatePct >= 50 }
    };
}

function printPlayback(r) {
    console.log(`\n## MB-03 playback (${r.options.minutes} min, progress every ${r.options.progressEvery} s, reads every ${r.options.readEvery} s)\n`);
    console.log(`Movie stopped at ${r.movie.stopPct} % of its runtime; user data after restore: ${JSON.stringify(r.userData.afterRestore)}\n`);
    console.log('| Section | n | hit rate | threshold (ms) | median (ms) | p95 (n >= 20) / max (n) |');
    console.log('|---|---|---|---|---|---|');
    for (const [k, s] of Object.entries(r.perSection)) { console.log(`| ${k} | ${s.n} | ${s.hitRatePct} % | ${fmt(s.threshold)} | ${fmt(s.median)} | ${s.tail} |`); }
    const o = r.overall;
    console.log(`| **overall** | ${o.n} | ${o.hitRatePct} % | | ${fmt(o.median)} | ${o.tail} |`);
    console.log(`\nAfter the stop: hit rate ${r.afterStop.now.hitRatePct} % (n ${r.afterStop.now.n}), 5 s later ${r.afterStop.plus5s.hitRatePct} % (n ${r.afterStop.plus5s.n})`);
    console.log(`Criterion ${r.criterion.rule}: ${r.criterion.pass ? 'PASS' : 'FAIL'} (${o.hitRatePct} %)`);
}

// ---------- scan ----------

async function refreshTask(admin) {
    const r = await call(admin, '/ScheduledTasks?isHidden=false');
    return (r.body || []).find((t) => t.Key === 'RefreshLibrary') || null;
}

/** Starts a library refresh and waits for the RefreshLibrary task to run and end; returns its start and end (ms). */
async function refreshAndWait(admin, timeoutMs, onRunning = null) {
    const before = await refreshTask(admin);
    if (!before) { throw new Error('no RefreshLibrary scheduled task'); }
    if (before.State !== 'Idle') { throw new Error(`RefreshLibrary is ${before.State}: wait for it before measuring`); }
    const lastEnd = before.LastExecutionResult ? before.LastExecutionResult.EndTimeUtc : null;
    const tCmd = Date.now();
    const r = await call(admin, '/Library/Refresh', { method: 'POST' });
    if (r.status >= 300) { throw new Error(`/Library/Refresh: HTTP ${r.status}`); }
    let tRunning = null;
    while (Date.now() - tCmd < timeoutMs) {
        const t = await refreshTask(admin);
        if (t.State === 'Running' && tRunning === null) { tRunning = Date.now(); if (onRunning) { onRunning(); } }
        const ended = t.LastExecutionResult && t.LastExecutionResult.EndTimeUtc !== lastEnd;
        if (t.State === 'Idle' && (tRunning !== null || ended)) {
            return { tCmd, tRunning: tRunning ?? tCmd, tEnd: Date.now(), status: t.LastExecutionResult ? t.LastExecutionResult.Status : null };
        }
        await sleep(1000);
    }
    throw new Error(`RefreshLibrary still running after ${timeoutMs / 60000} min`);
}

async function countPlaceholders(admin) {
    const r = await call(admin, '/Items?Recursive=true&IncludeItemTypes=Movie&SearchTerm=JuxBench%20Movie&Limit=0&EnableTotalRecordCount=true');
    return r.body ? r.body.TotalRecordCount : null;
}

async function cleanupScan(admin) {
    const names = leftovers();
    for (const n of names) { rmSync(path.join(opt.scanDir, n), { recursive: true, force: true }); }
    const refresh = await refreshAndWait(admin, opt.scanTimeoutMin * 60000);
    return { removed: names.length, refreshS: round1((refresh.tEnd - refresh.tCmd) / 1000), placeholdersLeftInLibrary: await countPlaceholders(admin) };
}

async function runScan() {
    if (!existsSync(opt.scanDir)) { throw new Error(`--scan-dir-host ${opt.scanDir} does not exist`); }
    const admin = await signIn('admin');
    if (leftovers().length > 0) { throw new Error(`${opt.scanDir} holds placeholders of an earlier run: rerun with --cleanup`); }
    const readers = [];
    for (let i = 0; i < opt.readers; i++) { readers.push(await signIn('user')); }
    const sections = await cachedSections(readers[0]);
    if (sections.length === 0) { throw new Error('no cached section in the layout'); }

    // Warm baseline: 20 passes, each section read once per pass.
    const warm = Object.fromEntries(sections.map((s) => [s.key, []]));
    for (const sec of sections) { await timedRead(readers[0], sec); }
    for (let i = 0; i < 20; i++) { for (const sec of sections) { warm[sec.key].push((await timedRead(readers[0], sec)).ms); } }
    const warmStats = Object.fromEntries(Object.entries(warm).map(([k, v]) => [k, stats(v)]));

    // Placeholders: a few bytes each, in their own movie folder.
    for (let i = 1; i <= opt.count; i++) {
        const name = placeholderName(i);
        mkdirSync(path.join(opt.scanDir, name), { recursive: true });
        writeFileSync(path.join(opt.scanDir, name, `${name}.mkv`), 'jux-mb03');
    }
    cleanups.push(() => cleanupScan(admin));
    const logSince = new Date().toISOString();

    const reads = [];
    let scanning = false;
    let done = false;
    let t0 = null;
    const loop = async (s, idx) => {
        while (!scanning && !done) { await sleep(200); }
        await sleep((idx * opt.readStep * 1000) / opt.readers);
        for (let k = 0; !done; k++) {
            const passStart = Date.now();
            for (const sec of sections) {
                if (done) { break; }
                const x = await timedRead(s, sec);
                const limit = 2 * (warmStats[sec.key].p95 ?? warmStats[sec.key].max);
                reads.push({ reader: idx, tS: round1((Date.now() - t0) / 1000), key: sec.key, status: x.status, ms: round1(x.ms), cold: x.ms > limit });
            }
            await sleep(Math.max(0, passStart + opt.readStep * 1000 - Date.now()));
        }
    };
    const loops = readers.map((s, i) => loop(s, i));
    let refresh;
    try {
        refresh = await refreshAndWait(admin, opt.scanTimeoutMin * 60000, () => { t0 = Date.now(); scanning = true; });
    } finally {
        done = true;
        await Promise.all(loops);
    }
    const added = await countPlaceholders(admin);
    const log = logTail(logSince);
    cleanups.length = 0;
    const restored = await cleanupScan(admin);
    for (const s of readers) { await call(s, '/Sessions/Logout', { method: 'POST' }).catch(() => {}); }
    await call(admin, '/Sessions/Logout', { method: 'POST' }).catch(() => {});

    const all = stats(reads.map((r) => r.ms));
    const warmAll = stats(Object.values(warm).flat());
    const perSection = Object.fromEntries(sections.map((sec) => {
        const rs = reads.filter((r) => r.key === sec.key);
        const s = stats(rs.map((r) => r.ms));
        const w = warmStats[sec.key];
        return [sec.key, { type: sec.type, n: rs.length, cold: rs.filter((r) => r.cold).length, median: round1(s.median), tail: tail(s, (x) => x.toFixed(0)), warmMedian: round1(w.median), warmP95: round1(w.p95), medianOverWarmP95: w.p95 ? round1(s.median / w.p95) : null }];
    }));
    const cold = reads.filter((r) => r.cold).length;
    const ratios = Object.values(perSection).map((s) => s.medianOverWarmP95).filter((x) => x !== null);
    const worstMedianRatio = ratios.length ? Math.max(...ratios) : null;
    return {
        options: { count: opt.count, readStep: opt.readStep, readers: opt.readers, readersAreSameUser: true },
        scan: { startDelayS: round1((refresh.tRunning - refresh.tCmd) / 1000), durationS: round1((refresh.tEnd - refresh.tRunning) / 1000), status: refresh.status, placeholdersInLibraryAfterScan: added },
        sections: sections.map(({ key, type, additionalData }) => ({ key, type, additionalData })),
        warm: Object.fromEntries(Object.entries(warm).map(([k, v]) => [k, v.map(round1)])),
        reads, perSection,
        overall: { n: reads.length, cold, coldPct: reads.length ? round1((100 * cold) / reads.length) : null, median: round1(all.median), tail: tail(all, (x) => x.toFixed(0)), warmMedian: round1(warmAll.median), warmTail: tail(warmAll, (x) => x.toFixed(0)), worstSectionMedianOverWarmP95: worstMedianRatio },
        restored, serverLog: log,
        criterion: { rule: 'cold re-reads during the scan <= 2 x warm p95: share of reads above 2 x warm p95 of their section, and median during the scan / warm p95 <= 2 for every section', pass: worstMedianRatio !== null && worstMedianRatio <= 2 }
    };
}

function printScan(r) {
    console.log(`\n## MB-03 scan (${r.options.count} placeholders, ${r.options.readers} readers every ${r.options.readStep} s)\n`);
    console.log(`Scan: ${r.scan.durationS} s (status ${r.scan.status}), ${r.scan.placeholdersInLibraryAfterScan} placeholders in the library after it; restored: ${JSON.stringify(r.restored)}\n`);
    console.log('| Section | n | cold (> 2 x warm p95) | median scan (ms) | p95 (n >= 20) / max (n) | warm median | warm p95 | median / warm p95 |');
    console.log('|---|---|---|---|---|---|---|---|');
    for (const [k, s] of Object.entries(r.perSection)) { console.log(`| ${k} | ${s.n} | ${s.cold} | ${fmt(s.median)} | ${s.tail} | ${fmt(s.warmMedian)} | ${fmt(s.warmP95)} | ${s.medianOverWarmP95} |`); }
    const o = r.overall;
    console.log(`| **overall** | ${o.n} | ${o.cold} (${o.coldPct} %) | ${fmt(o.median)} | ${o.tail} | ${fmt(o.warmMedian)} | ${o.warmTail} | max ${o.worstSectionMedianOverWarmP95} |`);
    if (r.serverLog) { console.log(`\nServer log: ${r.serverLog.matched} matching lines since the placeholders were written (tail in the raw file)`); }
    console.log(`Criterion ${r.criterion.rule}: ${r.criterion.pass ? 'PASS' : 'FAIL'}`);
}

// ---------- main ----------

if (opt.cleanupOnly) {
    const admin = await signIn('admin');
    console.log(JSON.stringify(await cleanupScan(admin)));
    await call(admin, '/Sessions/Logout', { method: 'POST' }).catch(() => {});
    process.exit(0);
}

const series = await startSeries({ label: `perf-bench-mb03-${part}` });
const out = { at: new Date().toISOString(), server: url, container: CONTAINER, part, label, homepage: series.homepage ? { version: series.homepage.version, dllMd5: series.homepage.dllMd5, sessionTtlMinutes: series.homepage.sessionTtlMinutes } : null };
let failure = null;
try {
    if (part === 'playback' || part === 'both') { out.playback = await runPlayback(series); printPlayback(out.playback); }
    if (part === 'scan' || part === 'both') { out.scan = await runScan(); printScan(out.scan); }
} catch (e) {
    failure = e;
    out.error = e.message;
    for (const f of cleanups.reverse()) { try { await f(); } catch (c) { console.error('cleanup failed:', c.message); } }
}
const file = path.join(rawDir, `perf-bench-mb03-${part}-${label}.json`);
writeFileSync(file, JSON.stringify(out, null, 2));
console.log(`\nraw: ${path.relative(process.cwd(), file)} (DLL md5 ${out.homepage ? out.homepage.dllMd5 : 'n/a'})`);
if (failure) { throw failure; }
await endSeries(series);
