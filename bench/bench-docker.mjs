// Docker helpers for the benches that run on a named volume (REV-05): a disposable Jellyfin container whose
// /config is a copy of the reference volume, the plugins folder of the configuration measured, and the
// machine's media folders mounted read-only.
// The machine-specific paths live in a local file, never in the repository: bench/.work/bench-local.json
// (or the file JUX_BENCH_LOCAL names), for example
//   { "mediaMounts": ["E:/Films/Films:/media/films1"], "pluginDirs": { "native": "D:/bench/plugins-native" } }
// Every media mount is read-only; the container paths must match the libraries of the reference volume.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { repoRoot, sleep } from './perf-bench-common.mjs';

export const localFile = path.resolve(repoRoot, process.env.JUX_BENCH_LOCAL || path.join('bench', '.work', 'bench-local.json'));

export function docker(args, opts = {}) {
    return execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024, ...opts }).trim();
}

/** Same as docker(), but returns null instead of throwing when the command fails (missing volume or container). */
export function dockerOrNull(args) {
    try { return docker(args); } catch { return null; }
}

export function readLocal() {
    if (!existsSync(localFile)) { throw new Error(`Missing ${localFile}: list the media mounts and plugin folders of this machine there (see bench/README.md).`); }
    const local = JSON.parse(readFileSync(localFile, 'utf8'));
    if (!Array.isArray(local.mediaMounts) || local.mediaMounts.length === 0) { throw new Error(`${localFile}: mediaMounts must list at least one host:container pair.`); }
    return local;
}

// Server profiles (V5, PROC-11), as docker run flags. std: no limit. lowend: 1 vCPU / 1 GiB. 2c8g: 2 vCPU /
// 8 GiB. srvperso: the production server calibrated on 2026-10-25 (MB-27), SRV_CPUS=1.0 on 3 pinned cores with
// 12 GiB: the total throughput matches, a single thread stays 3.6 to 8 times faster than production (cite this
// limit with every srvperso result). A disk IOPS limit (--device-read-iops) only applies to the WSL2 disk of
// Docker, never to a Windows bind mount: it is not part of any profile.
export const PROFILES = {
    std: () => [],
    lowend: () => ['--cpus', '1', '--memory', '1g', '--memory-swap', '1g'],
    '2c8g': () => ['--cpus', '2', '--memory', '8g', '--memory-swap', '8g'],
    srvperso: () => ['--cpuset-cpus', '0-2', '--cpus', process.env.SRV_CPUS || '1.0', '--memory', '12g', '--memory-swap', '12g']
};

export function profileLimits(profile) {
    const p = PROFILES[profile];
    if (!p) { throw new Error(`unknown profile ${profile} (${Object.keys(PROFILES).join(', ')})`); }
    return p();
}

export function volumeExists(name) { return dockerOrNull(['volume', 'inspect', name]) !== null; }

/** An empty plugins folder, for a server with no plugin at all (native configuration, first opening of a volume). */
export function emptyPluginsDir() {
    const dir = path.join(repoRoot, 'bench', '.work', 'plugins-empty');
    mkdirSync(dir, { recursive: true });
    return dir;
}

/**
 * Starts a disposable Jellyfin container on a config volume. The cache stays in the container layer, so
 * each start from a fresh volume copy begins with an empty image cache.
 * @param {{name: string, port: number|string, image: string, configVolume: string, pluginsDir: string, limits?: string[]}} o limits: extra docker run flags
 * @returns {string} the container start time (RFC 3339, from docker inspect)
 */
export function startBench({ name, port, image, configVolume, pluginsDir, limits = [] }) {
    const { mediaMounts } = readLocal();
    dockerOrNull(['rm', '-f', name]);
    docker(['run', '-d', '--name', name, '--restart', 'no', '-p', `${port}:8096`, ...limits,
        '-e', `JELLYFIN_PublishedServerUrl=http://localhost:${port}`,
        '-v', `${configVolume}:/config`, '-v', `${path.resolve(pluginsDir)}:/config/plugins`,
        ...mediaMounts.flatMap((m) => ['-v', `${m}:ro`]), image]);
    return docker(['inspect', name, '--format={{.State.StartedAt}}']);
}

/** Waits for a log line written after `since` (container start time), e.g. "Startup complete". */
export async function waitForLog(name, since, pattern, timeoutMs = 1800000) {
    const t0 = Date.now();
    while (Date.now() - t0 < timeoutMs) {
        const state = dockerOrNull(['inspect', name, '--format={{.State.Status}}']);
        if (state !== 'running') { throw new Error(`${name} is ${state} while waiting for ${pattern}`); }
        const logs = docker(['logs', '--since', since, name], { stdio: ['ignore', 'pipe', 'pipe'] });
        const line = logs.split(/\r?\n/).find((l) => pattern.test(l));
        if (line) { return { line, ms: Date.now() - t0 }; }
        await sleep(2000);
    }
    throw new Error(`${name}: no line ${pattern} within ${timeoutMs / 1000} s`);
}

/** Time left before a server started at `startedMs` (host clock, read when `docker restart` returned) is `minUptimeS` old. */
export function remainingUptimeMs(startedMs, minUptimeS, nowMs = Date.now()) {
    return Math.max(0, minUptimeS * 1000 - (nowMs - startedMs));
}

export function stopBench(name) {
    dockerOrNull(['stop', '-t', '30', name]);
    dockerOrNull(['rm', '-f', name]);
}
