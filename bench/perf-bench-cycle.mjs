// Browser measurement cycles: each cycle restarts the container, measures memory at rest, runs one
// cold page (fresh browser context, empty HTTP cache, server memory caches empty) then N warm pages
// (same context, warm HTTP cache, warm server caches).
//   node bench/perf-bench-cycle.mjs --profile std|lowend --viewport desktop|mobile --cycles 5 --warm 5 [--banner]
// The selectors are validated on the target first (selectors.json); --banner also requires a loaded banner in
// the first row (the banner-first profile, bench-start.mjs --banner). mobile = 390 x 844 at DPR 3.
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { rawDir, restartAndMeasure, dockerStats, sleep } from './perf-bench-common.mjs';
import { launch, ensureStorageState, storageStatePath, measurePage, validateTarget, VIEWPORTS, CONTEXTS } from './perf-bench-browser.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';

const series = await startSeries({ label: 'perf-bench-cycle' });

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const profile = arg('profile', 'std');
const viewport = arg('viewport', 'desktop');
const cycles = Number(arg('cycles', 5));
const warm = Number(arg('warm', 5));
const settleMs = Number(arg('settle', 30000));
const lowend = profile === 'lowend';
const timeoutMs = lowend ? 300000 : 120000;
const scratch = process.env.PERF_SCRATCH || rawDir;
const out = path.join(rawDir, `perf-bench-browser-${profile}-${viewport}.json`);

const log = (...a) => console.log(new Date().toISOString(), ...a);
const browser = await launch();
const banner = process.argv.includes('--banner');
const results = { profile, viewport, viewportSize: VIEWPORTS[viewport], context: CONTEXTS[viewport], lowend, banner, cycles: [] };
try {
    const state = await ensureStorageState(browser, storageStatePath(scratch));
    const probe = await browser.newContext({ storageState: state, ...CONTEXTS[viewport], locale: 'en-US' });
    try {
        results.selectors = await validateTarget(probe, { banner, timeoutMs });
        writeFileSync(path.join(rawDir, `selectors-${viewport}.json`), JSON.stringify(results.selectors, null, 1));
    } finally {
        await probe.close();
    }
    for (let c = 1; c <= cycles; c++) {
        const cycle = { cycle: c };
        cycle.startup = await restartAndMeasure();
        await sleep(settleMs);
        cycle.memRest = dockerStats();
        log(profile, viewport, 'cycle', c, 'restarted, mem', cycle.memRest.memMiB);
        const context = await browser.newContext({ storageState: state, ...CONTEXTS[viewport], locale: 'en-US' });
        try {
            cycle.cold = await measurePage(context, { lowend, timeoutMs, afterHome: async () => ({ mem: dockerStats() }) });
            cycle.memAfterTabs = dockerStats();
            log('cold first', cycle.cold.home.firstSectionMs, 'all', cycle.cold.home.allSectionsMs);
            cycle.warm = [];
            for (let w = 1; w <= warm; w++) {
                try {
                    const r = await measurePage(context, { lowend, timeoutMs });
                    cycle.warm.push(r);
                    log('warm', w, r.home.firstSectionMs, r.home.allSectionsMs);
                } catch (e) {
                    cycle.warm.push({ error: String(e.message || e).slice(0, 500) });
                    log('warm error', w, e.message);
                }
            }
        } catch (e) {
            cycle.error = String(e.message || e).slice(0, 500);
            log('cycle error', e.message);
        } finally {
            await context.close().catch(() => {});
        }
        results.cycles.push(cycle);
        writeFileSync(out, JSON.stringify(results));
    }
} finally {
    await browser.close();
}
log('done', out);
await endSeries(series);
