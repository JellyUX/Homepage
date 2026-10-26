// Fingerprint of a bench series (PROC-02): what was measured, so two series are compared only when they ran
// on the same data set and the same configuration. Every perf script writes it at the top of its raw folder
// (fingerprint.json) when the series starts and checks it when the series ends (fingerprint-end.json):
// - data set: /Items/Counts, images and providers per movie / series / episode, the bench user's played,
//   liked (the JellyUX watchlist), in-progress and favourite movies;
// - server: version, image, plugins and versions, plugin repositories, md5 of the Homepage DLL;
// - Homepage configuration: SessionTtlMinutes, featured and enabled sections, the deviations from the
//   product defaults (PROC-01.c);
// - container: CPU and memory limits, storage of /config (named volume or bind folder);
// - warm-up: when the HomeWarmupService line "Built the home screen" was written after the last start.
// A folder holds one series: a second script started in the same folder must find the same fingerprint.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { readCredentials, authenticate, api, serverUrl, rawDir, CONTAINER } from './perf-bench-common.mjs';
import { docker, dockerOrNull } from './bench-docker.mjs';

export const HOMEPAGE_PLUGIN_ID = '3adf1f1f-1541-4e47-b9e3-34d2d2968af6';
export const PRODUCT_DEFAULTS = { sessionTtlMinutes: 15, featuredSections: 0 };
// Fields expected to change between two series that are still comparable: the build under test (the point of
// a before / after comparison), the clock, the warm-up instant, the other containers (checked on their own,
// V4) and the names of the container, its port and its working volume (two copies of the template hold the
// same data: the counts, shapes and user history prove it). Everything else must be equal. A path ending in
// "." covers the whole subtree.
export const VARIABLE_FIELDS = ['capturedAt', 'server.startedAt', 'server.url', 'server.container', 'server.storage.source', 'homepage.dllMd5', 'homepage.version', 'plugins.JellyUX Homepage', 'warmup.builtAt', 'warmup.afterStartMs', 'otherContainers', 'label'];

const round2 = (x) => Math.round(x * 100) / 100;

/** The rows of a Homepage configuration, normalized: what a series shows does not depend on their order in the file. */
export function widgetRows(config) {
    return config.Widgets
        .map((w) => [w.WidgetType, w.Enabled, w.Featured, w.MinItems, w.MaxItems, w.SortBy, w.SortOrder, w.ViewMode, w.Order,
            (w.ExtraParams || []).map((p) => `${p.Key}=${p.Value}`).sort().join('&')].join('|'))
        .sort();
}

async function typeShape(session, type) {
    const body = (await api(session, `/Items?Recursive=true&IncludeItemTypes=${type}&Fields=ProviderIds&EnableUserData=false&EnableTotalRecordCount=true`)).body;
    const items = body.Items || [];
    if (items.length === 0) { return { n: 0 }; }
    const imagesOf = (i) => Object.keys(i.ImageTags || {}).length + (i.BackdropImageTags || []).length;
    return {
        n: items.length,
        imagesPerItem: round2(items.reduce((a, i) => a + imagesOf(i), 0) / items.length),
        providersPerItem: round2(items.reduce((a, i) => a + Object.keys(i.ProviderIds || {}).length, 0) / items.length),
        withAtLeast3Images: items.filter((i) => imagesOf(i) >= 3).length
    };
}

async function userShape(session) {
    const count = async (filter) => (await api(session, `/Items?Recursive=true&IncludeItemTypes=Movie&${filter}&Limit=0&EnableTotalRecordCount=true`)).body.TotalRecordCount;
    return {
        name: readCredentials().user.name,
        playedMovies: await count('IsPlayed=true'),
        likedMovies: await count('Filters=Likes'),
        inProgressMovies: await count('Filters=IsResumable'),
        favouriteMovies: await count('Filters=IsFavorite')
    };
}

/** md5 of the Homepage DLL inside the container, or null when the plugin is not installed. */
export function homepageDllMd5(container = CONTAINER) {
    // Every installed copy: an automatic update adds a second version folder before the next restart.
    const out = dockerOrNull(['exec', container, 'sh', '-c', 'md5sum /config/plugins/*/Jellyfin.Plugin.JuxHomepage.dll 2>/dev/null | cut -c1-32']);
    return out ? out.split(/\r?\n/).filter(Boolean).sort().join(',') : null;
}

function containerShape(container) {
    const j = JSON.parse(docker(['inspect', container]))[0];
    const config = (j.Mounts || []).find((m) => m.Destination === '/config');
    return {
        startedAt: j.State.StartedAt,
        image: j.Config.Image,
        imageId: j.Image,
        limits: { nanoCpus: j.HostConfig.NanoCpus, cpusetCpus: j.HostConfig.CpusetCpus || '', memory: j.HostConfig.Memory, memorySwap: j.HostConfig.MemorySwap },
        storage: config ? { type: config.Type, source: config.Type === 'volume' ? config.Name : config.Source } : null
    };
}

/** Time of the last "Built the home screen" line written after the container start, or null. */
function warmupShape(container, startedAt) {
    const logs = dockerOrNull(['logs', '-t', '--since', startedAt, container]) || '';
    const line = logs.split(/\r?\n/).reverse().find((l) => /Built the home screen/.test(l));
    if (!line) { return { built: false, builtAt: null }; }
    const at = line.slice(0, 30).split(' ')[0];
    return { built: true, builtAt: at, afterStartMs: Date.parse(at) - Date.parse(startedAt) };
}

/**
 * Reads the fingerprint of the server on JUX_URL (container JUX_CONTAINER).
 * @param {{label?: string, overrides?: string[]}} o overrides: the changes to the product defaults the series
 *   applied on purpose (e.g. "one featured section"), listed as they were applied; the deviations measured
 *   from the defaults are listed next to them, so a deviation nobody applied stands out. Default: the
 *   JUX_BENCH_OVERRIDES variable, items separated by ";".
 */
export async function collectFingerprint({ label = '', overrides = (process.env.JUX_BENCH_OVERRIDES || '').split(';').filter(Boolean) } = {}) {
    const creds = readCredentials();
    const admin = await authenticate(creds.admin);
    const user = await authenticate(creds.user);
    try {
        const c = containerShape(CONTAINER);
        const plugins = (await api(admin, '/Plugins')).body;
        const homepage = plugins.find((p) => p.Id.replace(/-/g, '') === HOMEPAGE_PLUGIN_ID.replace(/-/g, ''));
        let config = null;
        if (homepage) {
            const res = await api(admin, `/Plugins/${HOMEPAGE_PLUGIN_ID}/Configuration`);
            if (homepage.Status !== 'Active' || res.status !== 200) { throw new Error(`JellyUX Homepage is ${homepage.Status} (configuration HTTP ${res.status}): fix the bench before measuring`); }
            config = res.body;
        }
        const sessionTtlMinutes = config ? config.Cache.SessionTtlMinutes : null;
        const featuredSections = config ? config.Widgets.filter((w) => w.Enabled && w.Featured).length : null;
        const deviations = [];
        if (config && sessionTtlMinutes !== PRODUCT_DEFAULTS.sessionTtlMinutes) { deviations.push(`SessionTtlMinutes ${sessionTtlMinutes} (product ${PRODUCT_DEFAULTS.sessionTtlMinutes})`); }
        if (config && featuredSections !== PRODUCT_DEFAULTS.featuredSections) { deviations.push(`${featuredSections} featured sections (product ${PRODUCT_DEFAULTS.featuredSections})`); }
        const others = (docker(['ps', '--format={{.Names}}']).split(/\r?\n/)).filter((n) => n && n !== CONTAINER).sort();
        return {
            label,
            capturedAt: new Date().toISOString(),
            server: { url: serverUrl, container: CONTAINER, version: (await api(admin, '/System/Info')).body.Version, ...c },
            counts: (await api(admin, '/Items/Counts')).body,
            shape: { Movie: await typeShape(admin, 'Movie'), Series: await typeShape(admin, 'Series'), Episode: await typeShape(admin, 'Episode') },
            user: await userShape(user),
            plugins: Object.fromEntries(plugins.map((p) => [p.Name, `${p.Version} ${p.Status}`]).sort()),
            repositories: (await api(admin, '/Repositories')).body.map((r) => r.Url).sort(),
            homepage: homepage ? { version: homepage.Version, dllMd5: homepageDllMd5(CONTAINER), sessionTtlMinutes, featuredSections, enabledSections: config.Widgets.filter((w) => w.Enabled).length, widgets: widgetRows(config), cache: config.Cache, deviations, overrides } : null,
            warmup: warmupShape(CONTAINER, c.startedAt),
            otherContainers: others
        };
    } finally {
        await api(admin, '/Sessions/Logout', { method: 'POST' }).catch(() => {});
        await api(user, '/Sessions/Logout', { method: 'POST' }).catch(() => {});
    }
}

function flatten(value, prefix = '', out = {}) {
    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
        for (const [k, v] of Object.entries(value)) { flatten(v, prefix ? `${prefix}.${k}` : k, out); }
    } else {
        out[prefix] = Array.isArray(value) ? JSON.stringify(value) : value;
    }
    return out;
}

const isVariable = (key, variable) => variable.some((v) => (v.endsWith('.') ? key.startsWith(v) || key === v.slice(0, -1) : key === v));

/** Differences between two fingerprints outside the variable fields (empty: comparable). */
export function compareFingerprints(a, b, variable = VARIABLE_FIELDS) {
    const fa = flatten(a); const fb = flatten(b);
    const keys = [...new Set([...Object.keys(fa), ...Object.keys(fb)])].sort();
    return keys.filter((k) => !isVariable(k, variable) && fa[k] !== fb[k]).map((k) => `${k}: ${fa[k]} | ${fb[k]}`);
}

export const fingerprintFile = (dir = rawDir) => path.join(dir, 'fingerprint.json');

/**
 * Starts a series in rawDir: writes fingerprint.json, or checks it against the one already there (a folder
 * holds one series). Throws when the folder holds a series with another fingerprint.
 */
export async function startSeries(opts = {}) {
    const fp = await collectFingerprint(opts);
    const file = fingerprintFile();
    if (existsSync(file)) {
        const diff = compareFingerprints(JSON.parse(readFileSync(file, 'utf8')), fp);
        if (diff.length > 0) { throw new Error(`${rawDir} holds another series (fingerprint differs): ${diff.join('; ')}`); }
    } else {
        writeFileSync(file, JSON.stringify(fp, null, 2));
    }
    return fp;
}

/**
 * Ends a series: writes fingerprint-end.json and throws when the Homepage DLL changed during the series (an
 * automatic plugin update measures a release instead of the build under test, PROC-02.e) or when the item
 * counts changed.
 */
export async function endSeries(start, opts = {}) {
    const end = await collectFingerprint(opts);
    writeFileSync(path.join(rawDir, 'fingerprint-end.json'), JSON.stringify(end, null, 2));
    const before = start.homepage && start.homepage.dllMd5;
    const after = end.homepage && end.homepage.dllMd5;
    if (before !== after) { throw new Error(`Homepage DLL changed during the series: ${before} -> ${after}`); }
    const plugins = compareFingerprints({ plugins: start.plugins, homepage: start.homepage && start.homepage.version }, { plugins: end.plugins, homepage: end.homepage && end.homepage.version }, []);
    if (plugins.length > 0) { throw new Error(`plugins changed during the series: ${plugins.join('; ')}`); }
    // The media folders are a real library: a scheduled scan during the series would change the data set.
    const counts = compareFingerprints({ counts: start.counts }, { counts: end.counts }, []);
    if (counts.length > 0) { throw new Error(`item counts changed during the series: ${counts.join('; ')}`); }
    return end;
}
