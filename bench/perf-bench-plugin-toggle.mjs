// Memory and startup with JellyUX disabled: Disable, N restarts (settle then docker stats), then
// Enable and one restart. The finally block always re-enables the plugin.
// Observed on the bench (std run, 2026-10-14): after Disable + restart, Jellyfin 10.11.10 deleted the
// "JellyUX Homepage_<version>" folder and Enable then answered 404. The script therefore copies the
// installed folder to --backup before disabling and copies it back (meta.json status Active) when the
// folder is missing at the end, before the final restart.
//   node bench/perf-bench-plugin-toggle.mjs --profile lowend --cycles 3 --backup <scratch dir>
import { writeFileSync, cpSync, existsSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { readCredentials, authenticate, api, rawDir, repoRoot, restartAndMeasure, dockerStats, sleep } from './perf-bench-common.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';

const series = await startSeries({ label: 'perf-bench-plugin-toggle' });

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const cycles = Number(arg('cycles', 5));
const profile = arg('profile', 'std');
const settleMs = Number(arg('settle', 30000));
const backupRoot = arg('backup', null);
const JUX = '3adf1f1f15414e47b9e334d2d2968af6';
const log = (...a) => console.log(new Date().toISOString(), ...a);

const creds = readCredentials();
let admin = await authenticate(creds.admin);
const plugins = async () => (await api(admin, '/Plugins')).body.map((p) => ({ name: p.Name, version: p.Version, status: p.Status, id: p.Id }));
const result = { before: await plugins(), disabled: [], after: null, folderRestoredFromBackup: false };
const jux = result.before.find((p) => p.id === JUX);
if (!jux || jux.status !== 'Active') { throw new Error('JellyUX not active at start, aborting'); }
if (!backupRoot) { throw new Error('--backup is required'); }
// Found by the guid of its meta.json: a local build reports its assembly version (0.1.0.0) while the
// folder keeps the installed release's name.
const pluginsRoot = path.join(repoRoot, 'docker', 'plugins');
const folderName = readdirSync(pluginsRoot).find((name) => {
    const meta = path.join(pluginsRoot, name, 'meta.json');
    return existsSync(meta) && JSON.parse(readFileSync(meta, 'utf8')).guid?.replace(/-/g, '') === JUX
        && JSON.parse(readFileSync(meta, 'utf8')).status === 'Active';
});
if (!folderName) { throw new Error(`no active JellyUX folder in ${pluginsRoot}`); }
const pluginDir = path.join(pluginsRoot, folderName);
const backupDir = path.join(backupRoot, 'plugin-backup', folderName);
// The plugin API addresses the installed version (meta.json), not the assembly version /Plugins reports.
const version = JSON.parse(readFileSync(path.join(pluginDir, 'meta.json'), 'utf8')).version;
rmSync(backupDir, { recursive: true, force: true });
cpSync(pluginDir, backupDir, { recursive: true });
log('backup', backupDir);
try {
    const dis = await api(admin, `/Plugins/${JUX}/${version}/Disable`, { method: 'POST' });
    log('disable', dis.status);
    if (dis.status !== 204) { throw new Error(`Disable answered ${dis.status}: the plugin is still enabled, nothing measured`); }
    for (let c = 1; c <= cycles; c++) {
        const st = await restartAndMeasure();
        await sleep(settleMs);
        st.memRest = dockerStats();
        admin = await authenticate(creds.admin);
        st.juxStatus = (await plugins()).find((p) => p.id === JUX)?.status || 'absent';
        st.pluginFolderPresent = existsSync(pluginDir);
        result.disabled.push(st);
        log('disabled cycle', c, st.memRest.memMiB, st.juxStatus, 'folder', st.pluginFolderPresent);
    }
} finally {
    admin = await authenticate(creds.admin);
    const en = await api(admin, `/Plugins/${JUX}/${version}/Enable`, { method: 'POST' });
    log('enable', en.status);
    if (en.status !== 204 || !existsSync(path.join(pluginDir, 'meta.json'))) {
        rmSync(pluginDir, { recursive: true, force: true });
        cpSync(backupDir, pluginDir, { recursive: true });
        result.folderRestoredFromBackup = true;
        log('folder restored from backup, meta status', JSON.parse(readFileSync(path.join(pluginDir, 'meta.json'), 'utf8')).status);
    }
    const st = await restartAndMeasure();
    admin = await authenticate(creds.admin);
    result.after = { startup: st, plugins: await plugins() };
    log('after', JSON.stringify(result.after.plugins.map((p) => `${p.name} ${p.version} ${p.status}`)), st.loadSectionsLine);
    writeFileSync(path.join(rawDir, `perf-bench-plugin-toggle-${profile}.json`), JSON.stringify({ profile, ...result }, null, 1));
}
await endSeries(series);
