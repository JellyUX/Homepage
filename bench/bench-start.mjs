// Starts a bench container on a working volume with a server profile, on the product defaults (PROC-01.c), and
// waits until it is ready to measure: /health, then the HomeWarmupService line "Built the home screen" when
// Homepage is installed (the same fixed wait otherwise, so native and Homepage start measuring alike).
// Ported from the audit V5 start-warm.sh.
//   node bench/bench-start.mjs --name <container> --port <port> --volume <working volume> --plugins <key|folder>
//        [--profile std|lowend|2c8g|srvperso] [--image jellyfin/jellyfin:10.11.11] [--banner] [--wait-s 300]
// --plugins: a key of pluginDirs in bench/.work/bench-local.json, or a folder. The folder is copied to
// bench/.work/plugins-run/<container> without the Homepage configuration file, so every series starts on the
// defaults of the build under test. --banner adds the one deviation the "banner first" profile needs (a
// featured top-rated movie section above every other row), applied through the API before a restart; the
// fingerprint lists it (JUX_BENCH_OVERRIDES).
import { cpSync, existsSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readCredentials, authenticate, api, repoRoot, sleep } from './perf-bench-common.mjs';
import { docker, readLocal, profileLimits, startBench, waitForLog, stopBench } from './bench-docker.mjs';
import { HOMEPAGE_PLUGIN_ID } from './bench-fingerprint.mjs';

export const BANNER_OVERRIDE = 'banner first: featured jux.local.top-rated (Movie) above every row';
const HOMEPAGE_CONFIG = 'Jellyfin.Plugin.JuxHomepage.xml';

/** Copies a plugins folder for one run, without the Homepage configuration (defaults of the build). */
export function preparePlugins(source, container) {
    const dir = path.join(repoRoot, 'bench', '.work', 'plugins-run', container);
    rmSync(dir, { recursive: true, force: true });
    cpSync(source, dir, { recursive: true });
    rmSync(path.join(dir, 'configurations', HOMEPAGE_CONFIG), { force: true });
    return dir;
}

export function resolvePlugins(keyOrFolder) {
    const dirs = readLocal().pluginDirs || {};
    const dir = dirs[keyOrFolder] || keyOrFolder;
    if (!existsSync(dir)) { throw new Error(`plugins folder not found: ${dir}`); }
    return dir;
}

async function waitHealthy(base, timeoutMs = 300000) {
    const t0 = Date.now();
    while (Date.now() - t0 < timeoutMs) {
        try {
            const res = await fetch(`${base}/health`, { signal: AbortSignal.timeout(3000) });
            if (res.ok && (await res.text()).includes('Healthy')) { return Date.now() - t0; }
        } catch { /* not up yet */ }
        await sleep(1000);
    }
    throw new Error(`${base} not healthy within ${timeoutMs / 1000} s`);
}

function hasHomepage(pluginsDir) {
    return docker(['run', '--rm', '--entrypoint', 'sh', '-v', `${pluginsDir}:/p:ro`, 'jellyfin/jellyfin:10.11.11', '-c', 'ls /p/*/Jellyfin.Plugin.JuxHomepage.dll 2>/dev/null | wc -l']) !== '0';
}

/** Waits for /health then for the warm-up line (Homepage) or the same fixed wait (no Homepage). */
async function waitReady({ name, base, since, homepage, waitS }) {
    const t0 = Date.now();
    const healthyMs = await waitHealthy(base);
    let warmup = null;
    if (homepage) {
        warmup = await waitForLog(name, since, /Built the home screen/, waitS * 1000).catch(() => null);
    } else {
        await sleep(Math.max(0, waitS * 1000 - (Date.now() - t0)));
    }
    return { healthyMs, readyMs: Date.now() - t0, warmupLine: warmup ? warmup.line.trim() : null };
}

async function applyBanner(base) {
    const admin = await authenticate(readCredentials().admin, base);
    try {
        const config = (await api(admin, `/Plugins/${HOMEPAGE_PLUGIN_ID}/Configuration`)).body;
        const first = Math.min(...config.Widgets.map((w) => w.Order));
        config.Widgets.push({
            WidgetType: 'jux.local.top-rated', Enabled: true, Order: first - 1, MinItems: 1, MaxItems: 20, ViewMode: 'Landscape',
            SortBy: 'SortName', SortOrder: 'Ascending', Featured: true, FeaturedMinRating: 0, ExtraParams: [{ Key: 'itemTypes', Value: 'Movie' }]
        });
        const res = await api(admin, `/Plugins/${HOMEPAGE_PLUGIN_ID}/Configuration`, { method: 'POST', body: JSON.stringify(config) });
        if (res.status >= 300) { throw new Error(`banner configuration refused: HTTP ${res.status}`); }
    } finally {
        await api(admin, '/Sessions/Logout', { method: 'POST' }).catch(() => {});
    }
}

/**
 * @param {{name: string, port: number|string, volume: string, plugins: string, profile?: string, image?: string, banner?: boolean, waitS?: number}} o
 */
export async function startProfiled({ name, port, volume, plugins, profile = 'std', image = 'jellyfin/jellyfin:10.11.11', banner = false, waitS = 300 }) {
    const base = `http://localhost:${port}`;
    const pluginsDir = preparePlugins(resolvePlugins(plugins), name);
    const homepage = hasHomepage(pluginsDir);
    if (banner && !homepage) { throw new Error('--banner needs Homepage in the plugins folder'); }
    let since = startBench({ name, port, image, configVolume: volume, pluginsDir, limits: profileLimits(profile) });
    let ready = await waitReady({ name, base, since, homepage, waitS });
    if (banner) {
        await applyBanner(base);
        docker(['restart', '-t', '30', name]);
        since = docker(['inspect', name, '--format={{.State.StartedAt}}']);
        ready = await waitReady({ name, base, since, homepage, waitS });
    }
    return { name, port, volume, plugins, profile, image, homepage, overrides: banner ? [BANNER_OVERRIDE] : [], startedAt: since, ...ready };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
    const o = { name: arg('name'), port: arg('port'), volume: arg('volume'), plugins: arg('plugins'), profile: arg('profile', 'std'), image: arg('image', 'jellyfin/jellyfin:10.11.11'), banner: process.argv.includes('--banner'), waitS: Number(arg('wait-s', 300)) };
    if (!o.name || !o.port || !o.volume || !o.plugins) { console.error('usage: node bench/bench-start.mjs --name <container> --port <port> --volume <volume> --plugins <key|folder> [--profile std] [--banner]'); process.exit(2); }
    try {
        console.log(JSON.stringify(await startProfiled(o)));
    } catch (e) {
        stopBench(o.name);
        throw e;
    }
}
