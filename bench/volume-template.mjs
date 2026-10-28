// Reference data set on a named Docker volume (REV-05): one template volume, copied before each series into
// a working volume per bench, so every bench starts from the same content. Non-interactive.
//   node bench/volume-template.mjs create --from <config folder> --name <volume> [--image jellyfin/jellyfin:10.11.11]
//   node bench/volume-template.mjs copy --from <volume> --to <volume>
//   node bench/volume-template.mjs verify <volume>
//   node bench/volume-template.mjs shape <volume> [--min-movie-images 3]
//   node bench/volume-template.mjs rescan <volume>
//   node bench/volume-template.mjs migrate --from <volume> --to <volume> --image jellyfin/jellyfin:12.3
//   node bench/volume-template.mjs mature --from <volume> --to <volume> --plugins <key|folder>
// create copies the folder (a Jellyfin /config without log/ and plugins/), opens it once with the image (a
// forward database migration: never open the volume with an older image afterwards), stops it and writes a
// stamp (.jux-template.json: item counts, file count, bytes, database sha256). verify compares a volume with
// its stamp, so a copy is proved equal to the template until a server opens it. migrate copies a volume,
// opens the copy with another image and refuses a migration that changes the item counts (PROC-02.f). rescan
// runs a library scan with the stamped image and stamps the result: a 10.11 server keeps the items whose file
// was deleted until a scan, a 12 migration drops them, so a template is rescanned before it is migrated.
// mature copies a template and runs it with a plugins folder until every interval task has run once (DR-004):
// Jellyfin runs an interval task that never ran one hour after the server start (TMDb Box Sets then creates
// collections), so a bench longer than one hour on an unmatured template changes the library mid-series.
// Bench logins: identifiants.txt (admin first). Media folders: bench/.work/bench-local.json (bench-docker.mjs).
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readCredentials, authenticate, api, sleep } from './perf-bench-common.mjs';
import { docker, volumeExists, emptyPluginsDir, startBench, waitForLog, stopBench } from './bench-docker.mjs';
import { preparePlugins, resolvePlugins } from './bench-start.mjs';
import { dbShape, checkMovieImages } from './db-shape.mjs';

const STAMP = '.jux-template.json';
const HELPER_PORT = process.env.JUX_TEMPLATE_PORT || '8199';
const HELPER = 'jux-bench-template-helper';
const DEFAULT_IMAGE = 'jellyfin/jellyfin:10.11.11';

const arg = (name, fallback) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : fallback; };

/** Runs a shell command in a throwaway container of `image` (any Jellyfin image has sh, tar and coreutils). */
function inContainer(image, mounts, script) {
    return docker(['run', '--rm', '--entrypoint', 'sh', ...mounts.flatMap((m) => ['-v', m]), image, '-c', script]);
}

/** File count, bytes and database sha256 of a volume, the stamp and the database WAL excluded. */
export function contentOf(volume, image = DEFAULT_IMAGE) {
    const out = inContainer(image, [`${volume}:/v:ro`],
        `cd /v && find . -type f ! -name ${STAMP} -printf '%s\\n' | awk '{n++; s+=$1} END {printf "%d %d\\n", n, s}'`
        + ' && sha256sum data/jellyfin.db | cut -c1-64'
        // Path, size and modification time of every file (cp -a keeps them): an edit of the same size still shows.
        + ` && find . -type f ! -name ${STAMP} -printf '%p %s %T@\\n' | sort | sha256sum | cut -c1-64`
        + ' && (ls data | grep -c "jellyfin.db-wal" || true)');
    const [counts, sha, listing, wal] = out.split(/\r?\n/);
    const [files, bytes] = counts.split(' ').map(Number);
    return { files, bytes, dbSha256: sha, listingSha256: listing, wal: Number(wal || 0) > 0 };
}

export function readStamp(volume, image = DEFAULT_IMAGE) {
    const text = inContainer(image, [`${volume}:/v:ro`], `cat /v/${STAMP} 2>/dev/null || true`);
    return text ? JSON.parse(text) : null;
}

function writeStamp(volume, stamp, image) {
    const dir = mkdtempSync(path.join(tmpdir(), 'jux-stamp-'));
    try {
        writeFileSync(path.join(dir, STAMP), JSON.stringify(stamp, null, 2));
        inContainer(image, [`${volume}:/v`, `${dir}:/s:ro`], `cp /s/${STAMP} /v/${STAMP}`);
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

/** Differences between a stamp and the content measured now (empty: same content). */
export function compareContent(stamp, now) {
    const diff = [];
    for (const k of ['files', 'bytes', 'dbSha256', 'listingSha256', 'wal']) {
        if (stamp.content[k] !== now[k]) { diff.push(`${k}: stamp ${stamp.content[k]}, now ${now[k]}`); }
    }
    return diff;
}

// ItemCount is 0 on 10.11.11 and the total of the other counts on 12.3 (measured on the template, 2026-10-26):
// a change of definition, not of content.
export const COUNTS_NOT_COMPARED = ['ItemCount'];

/** Differences between two /Items/Counts answers (empty: same counts). */
export function compareCounts(a, b) {
    const keys = [...new Set([...Object.keys(a), ...Object.keys(b)])].filter((k) => !COUNTS_NOT_COMPARED.includes(k)).sort();
    return keys.filter((k) => a[k] !== b[k]).map((k) => `${k}: ${a[k]} -> ${b[k]}`);
}

/** Runs the library scan and waits for its end (a scan removes the items whose file is gone). */
async function scanLibrary(admin) {
    const task = async () => (await api(admin, '/ScheduledTasks?isHidden=false')).body.find((t) => t.Key === 'RefreshLibrary');
    const t0 = Date.now();
    const before = (await task()).LastExecutionResult;
    await api(admin, '/Library/Refresh', { method: 'POST' });
    for (;;) {
        await sleep(5000);
        const t = await task();
        const last = t.LastExecutionResult;
        if (t.State === 'Idle' && last && (!before || last.EndTimeUtc !== before.EndTimeUtc)) { return { scanMs: Date.now() - t0, status: last.Status }; }
        if (Date.now() - t0 > 3600000) { throw new Error('library scan still running after 1 h'); }
    }
}

/** Opens a volume once with `image` (optionally scanning the library), reads the item counts, stops the server. */
async function openOnce(volume, image, { scan = false } = {}) {
    const t0 = Date.now();
    const since = startBench({ name: HELPER, port: HELPER_PORT, image, configVolume: volume, pluginsDir: emptyPluginsDir() });
    try {
        const startup = await waitForLog(HELPER, since, /Startup complete/);
        const base = `http://localhost:${HELPER_PORT}`;
        const admin = await authenticate(readCredentials().admin, base);
        const scanned = scan ? await scanLibrary(admin) : null;
        const counts = (await api(admin, '/Items/Counts')).body;
        const version = (await api(admin, '/System/Info')).body.Version;
        await api(admin, '/Sessions/Logout', { method: 'POST' });
        return { counts, version, scanned, startupMs: startup.ms, openMs: Date.now() - t0 };
    } finally {
        stopBench(HELPER);
    }
}

function copyVolume(from, to, image) {
    if (!volumeExists(from)) { throw new Error(`no volume ${from}`); }
    if (volumeExists(to)) { throw new Error(`volume ${to} already exists: remove it first (docker volume rm ${to}) or pick another name`); }
    docker(['volume', 'create', to]);
    const t0 = Date.now();
    inContainer(image, [`${from}:/src:ro`, `${to}:/dst`], 'cp -a /src/. /dst/');
    return Date.now() - t0;
}

async function create() {
    const from = arg('from'); const name = arg('name'); const image = arg('image', DEFAULT_IMAGE);
    if (!from || !name) { throw new Error('create needs --from <config folder> --name <volume>'); }
    if (volumeExists(name)) { throw new Error(`volume ${name} already exists`); }
    docker(['volume', 'create', name]);
    const t0 = Date.now();
    inContainer(image, [`${path.resolve(from)}:/src:ro`, `${name}:/dst`],
        'tar -C /src --exclude=./log --exclude=./plugins -cf - . | tar -C /dst -xf -');
    const copyMs = Date.now() - t0;
    const opened = await openOnce(name, image);
    const stamp = { source: path.resolve(from), image, createdAt: new Date().toISOString(), version: opened.version, counts: opened.counts, content: contentOf(name, image) };
    writeStamp(name, stamp, image);
    console.log(JSON.stringify({ volume: name, copyMs, firstOpenMs: opened.openMs, startupMs: opened.startupMs, stamp }, null, 2));
}

function copy() {
    const from = arg('from'); const to = arg('to'); const image = arg('image', DEFAULT_IMAGE);
    if (!from || !to) { throw new Error('copy needs --from <volume> --to <volume>'); }
    const copyMs = copyVolume(from, to, image);
    const diff = verifyOrList(to, image);
    console.log(JSON.stringify({ from, to, copyMs, equal: diff.length === 0, diff }, null, 2));
    if (diff.length > 0) { process.exitCode = 1; }
}

function verifyOrList(volume, image) {
    const stamp = readStamp(volume, image);
    if (!stamp) { return [`no ${STAMP} in ${volume}`]; }
    return compareContent(stamp, contentOf(volume, image));
}

function verify() {
    const volume = process.argv[3]; const image = arg('image', DEFAULT_IMAGE);
    const diff = verifyOrList(volume, image);
    console.log(JSON.stringify({ volume, equal: diff.length === 0, diff }, null, 2));
    if (diff.length > 0) { process.exitCode = 1; }
}

async function shape() {
    const volume = process.argv[3]; const image = arg('image', DEFAULT_IMAGE); const min = Number(arg('min-movie-images', 3));
    const dir = mkdtempSync(path.join(tmpdir(), 'jux-dbshape-'));
    try {
        inContainer(image, [`${volume}:/v:ro`, `${dir}:/out`], 'cp /v/data/jellyfin.db* /out/');
        const result = await dbShape(path.join(dir, 'jellyfin.db'));
        const problem = checkMovieImages(result, min);
        console.log(JSON.stringify({ volume, ...result, check: problem || `>= ${min} images per movie` }, null, 2));
        if (problem) { process.exitCode = 1; }
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

async function rescan() {
    const volume = process.argv[3];
    const before = readStamp(volume);
    if (!before) { throw new Error(`no ${STAMP} in ${volume}`); }
    const opened = await openOnce(volume, before.image, { scan: true });
    const stamp = { ...before, rescannedAt: new Date().toISOString(), countsBeforeRescan: before.counts, counts: opened.counts, content: contentOf(volume, before.image) };
    writeStamp(volume, stamp, before.image);
    console.log(JSON.stringify({ volume, ...opened.scanned, countsBefore: before.counts, countsAfter: opened.counts, diff: compareCounts(before.counts, opened.counts), stamp }, null, 2));
}

async function migrate() {
    const from = arg('from'); const to = arg('to'); const image = arg('image');
    if (!from || !to || !image) { throw new Error('migrate needs --from <volume> --to <volume> --image <image>'); }
    const before = readStamp(from);
    if (!before) { throw new Error(`no ${STAMP} in ${from}`); }
    const source = verifyOrList(from, before.image);
    if (source.length > 0) { throw new Error(`${from} differs from its stamp: ${source.join('; ')}`); }
    const copyMs = copyVolume(from, to, before.image);
    const opened = await openOnce(to, image);
    const diff = compareCounts(before.counts, opened.counts);
    const stamp = { source: from, image, createdAt: new Date().toISOString(), version: opened.version, counts: opened.counts, migratedFrom: { volume: from, image: before.image, version: before.version, counts: before.counts }, content: contentOf(to, image) };
    // A refused migration gets no stamp: verify and copy then refuse the volume.
    if (diff.length === 0) { writeStamp(to, stamp, image); }
    console.log(JSON.stringify({ from, to, copyMs, migrationOpenMs: opened.openMs, countsBefore: before.counts, countsAfter: opened.counts, countsEqual: diff.length === 0, diff }, null, 2));
    if (diff.length > 0) { process.exitCode = 1; }
}

/** Tasks a matured template still waits for: an interval task that never ran, or any task running now. */
export function pendingFirstRuns(tasks) {
    return tasks.filter((t) => t.State !== 'Idle'
        || (!t.LastExecutionResult && (t.Triggers || []).some((tr) => tr.Type === 'IntervalTrigger'))).map((t) => t.Name);
}

async function mature() {
    const from = arg('from'); const to = arg('to'); const plugins = arg('plugins');
    if (!from || !to || !plugins) { throw new Error('mature needs --from <volume> --to <volume> --plugins <key|folder>'); }
    const before = readStamp(from);
    if (!before) { throw new Error(`no ${STAMP} in ${from}`); }
    const source = verifyOrList(from, before.image);
    if (source.length > 0) { throw new Error(`${from} differs from its stamp: ${source.join('; ')}`); }
    const copyMs = copyVolume(from, to, before.image);
    const t0 = Date.now();
    const since = startBench({ name: HELPER, port: HELPER_PORT, image: before.image, configVolume: to, pluginsDir: preparePlugins(resolvePlugins(plugins), HELPER) });
    try {
        await waitForLog(HELPER, since, /Startup complete/);
        const base = `http://localhost:${HELPER_PORT}`;
        // The first runs are due one hour after the start: no deadline below a few hours, a progress line instead.
        let pending;
        for (;;) {
            const admin = await authenticate(readCredentials().admin, base);
            const tasks = (await api(admin, '/ScheduledTasks')).body;
            pending = pendingFirstRuns(tasks);
            if (pending.length === 0) {
                const counts = (await api(admin, '/Items/Counts')).body;
                await api(admin, '/Sessions/Logout', { method: 'POST' });
                stopBench(HELPER);
                const stamp = { ...before, maturedAt: new Date().toISOString(), maturedFrom: { volume: from, counts: before.counts }, maturedWith: path.basename(resolvePlugins(plugins)), tasks: tasks.map((t) => ({ name: t.Name, last: t.LastExecutionResult ? t.LastExecutionResult.EndTimeUtc : null })), counts, content: contentOf(to, before.image) };
                writeStamp(to, stamp, before.image);
                console.log(JSON.stringify({ from, to, copyMs, matureMs: Date.now() - t0, countsBefore: before.counts, countsAfter: counts, diff: compareCounts(before.counts, counts) }, null, 2));
                return;
            }
            await api(admin, '/Sessions/Logout', { method: 'POST' });
            console.log(`${new Date().toISOString()} ${Math.round((Date.now() - t0) / 60000)} min, waiting for: ${pending.join(', ')}`);
            if (Date.now() - t0 > 4 * 3600000) { throw new Error(`tasks still pending after 4 h: ${pending.join(', ')}`); }
            await sleep(60000);
        }
    } finally {
        stopBench(HELPER);
    }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const commands = { create, copy, verify, shape, rescan, migrate, mature };
    const command = commands[process.argv[2]];
    if (!command) { console.error(`usage: node bench/volume-template.mjs ${Object.keys(commands).join('|')} ...`); process.exit(2); }
    await command();
}
