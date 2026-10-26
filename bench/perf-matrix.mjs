// Load matrix (PROC-10): configurations x server profiles, each cell on a fresh copy of the reference volume,
// measured by bench/perf-matrix-cell.mjs. Ported from the audit V5 mx-run.sh.
//   node bench/perf-matrix.mjs --configs native,pile,full,hp --profiles lowend,srvperso [--template jux-bench-tpl-1011]
//        [--image jellyfin/jellyfin:10.11.11] [--out bench/.work/matrix] [--remove-volumes] [--banner]
//        [cell options: --browsers desktop,desktop-slow,phone --colds 3 --boot-colds 3 --warm-desktop 20 --warm-phone 10]
// A configuration is a pluginDirs key of bench/.work/bench-local.json (native: no plugin; pile: the production
// plugins without Homepage; full: with it; hp: Homepage alone). Per cell: the template is copied into
// jux-bench-mx-<config>-<profile> (refused when it exists, unless --remove-volumes removes it first, and again after the
// cell; the signed-in state of each cell is its own, a token of another copy is refused), the container jux-bench-mx starts on the profile and the product defaults (bench-start.mjs), the cell
// runs with its own raw folder <out>/<config>-<profile> (the native cell is the witness of the others, so
// run native in every matrix), and the folder is archived. Only one Jellyfin container may run (V4): the
// matrix refuses to start next to another one.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { repoRoot } from './perf-bench-common.mjs';
import { docker, dockerOrNull, volumeExists, stopBench } from './bench-docker.mjs';
import { startProfiled, BANNER_OVERRIDE } from './bench-start.mjs';
import { archiveSeries } from './bench-archive.mjs';

const NAME = 'jux-bench-mx';
const PORT = process.env.JUX_MATRIX_PORT || '8112';
const CELL_OPTIONS = ['browsers', 'colds', 'boot-colds', 'warm-desktop', 'warm-phone'];

/** Other running Jellyfin containers (any image jellyfin/jellyfin), which a measured cell must not share the PC with. */
export function otherJellyfins(except = NAME) {
    return docker(['ps', '--format={{.Names}} {{.Image}}']).split(/\r?\n/).filter((l) => / jellyfin\/jellyfin:/.test(l) && !l.startsWith(`${except} `)).map((l) => l.split(' ')[0]);
}

export function cellFolder(out, config, profile) { return path.join(out, `${config}-${profile}`); }

async function main() {
    const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
    const configs = arg('configs', 'native,hp').split(',');
    const profiles = arg('profiles', 'srvperso').split(',');
    const template = arg('template', 'jux-bench-tpl-1011');
    const image = arg('image', 'jellyfin/jellyfin:10.11.11');
    const out = path.resolve(repoRoot, arg('out', path.join('bench', '.work', 'matrix')));
    const removeVolumes = process.argv.includes('--remove-volumes');
    const banner = process.argv.includes('--banner');
    const others = otherJellyfins();
    if (others.length > 0) { throw new Error(`another Jellyfin container runs (${others.join(', ')}): stop it first (V4)`); }
    if (!configs.includes('native')) { console.warn('no native configuration: the other cells have no witness in this matrix'); }
    mkdirSync(out, { recursive: true });
    const cellArgs = CELL_OPTIONS.flatMap((o) => (arg(o) ? [`--${o}`, arg(o)] : []));
    for (const config of configs) {
        for (const profile of profiles) {
            const volume = `jux-bench-mx-${config}-${profile}`;
            const dir = cellFolder(out, config, profile);
            console.log(`== ${config} ${profile} ${new Date().toISOString()}`);
            if (volumeExists(volume)) {
                if (!removeVolumes) { throw new Error(`volume ${volume} exists: pass --remove-volumes to replace it`); }
                stopBench(NAME);
                docker(['volume', 'rm', volume]);
            }
            execFileSync(process.execPath, [path.join(repoRoot, 'bench', 'volume-template.mjs'), 'copy', '--from', template, '--to', volume, '--image', image], { stdio: 'inherit' });
            const useBanner = banner && config !== 'native' && config !== 'pile';
            try {
                // The fixed wait of a configuration without Homepage, and the longest wait for its warm-up line.
                const waitS = profile === 'lowend' ? 100 : 60;
                const started = await startProfiled({ name: NAME, port: PORT, volume, plugins: config, profile, image, banner: useBanner, waitS: waitS });
                console.log(JSON.stringify(started));
                if (existsSync(dir) && readdirSync(dir).length > 0) { throw new Error(`${dir} already holds a series: pick another --out`); }
                mkdirSync(dir, { recursive: true });
                const scratch = path.join(out, '.state', `${config}-${profile}`);
                rmSync(scratch, { recursive: true, force: true });
                execFileSync(process.execPath, ['--no-warnings', path.join(repoRoot, 'bench', 'perf-matrix-cell.mjs'), ...cellArgs, ...(useBanner ? ['--banner'] : []), '--wait-s', String(waitS)], {
                    stdio: 'inherit', timeout: 4 * 3600 * 1000,
                    env: { ...process.env, JUX_URL: `http://localhost:${PORT}`, JUX_CONTAINER: NAME, JUX_BENCH_OUT: dir, PERF_SCRATCH: scratch, JUX_BENCH_OVERRIDES: useBanner ? BANNER_OVERRIDE : '' }
                });
                console.log(JSON.stringify(archiveSeries(dir, path.join(out, 'archives'))));
            } finally {
                stopBench(NAME);
                if (removeVolumes && dockerOrNull(['volume', 'inspect', volume])) { docker(['volume', 'rm', volume]); }
            }
        }
    }
    console.log(`END ${new Date().toISOString()}`);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) { await main(); }
