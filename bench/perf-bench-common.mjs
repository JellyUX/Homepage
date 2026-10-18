// Shared helpers for the bench scripts. Credentials are read at run time from the gitignored
// identifiants.txt at the repository root and never written anywhere.
// JUX_URL picks the server (default http://localhost:8096), JUX_CONTAINER its Docker container (default
// jellyux-test), JUX_BENCH_OUT the folder raw results are written to (default TODO_V5/mesures/raw, not
// versioned).
import { mkdirSync, readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
export const repoRoot = path.resolve(here, '..');
export const rawDir = path.resolve(repoRoot, process.env.JUX_BENCH_OUT || path.join('TODO_V5', 'mesures', 'raw'));
mkdirSync(rawDir, { recursive: true });
export const serverUrl = process.env.JUX_URL || 'http://localhost:8096';
// Jellyfin revokes the previous token of a DeviceId on each AuthenticateByName: one DeviceId per run keeps
// two runs (or a run and a browser session) from logging each other out.
const AUTH = `MediaBrowser Client="jux-perf-bench", Device="bench", DeviceId="jux-perf-bench-${randomUUID()}", Version="1.0.0"`;

export function readCredentials() {
    const lines = readFileSync(path.join(repoRoot, 'identifiants.txt'), 'utf8').split(/\r?\n/);
    const users = [];
    let current = null;
    for (const line of lines) {
        const u = line.match(/username\s*:\s*(.+)$/);
        const p = line.match(/password\s*:\s*(.+)$/);
        if (u) { current = { name: u[1].trim() }; users.push(current); }
        if (p && current) { current.password = p[1].trim(); }
    }
    return { admin: users[0], user: users[1] };
}

export async function authenticate(user) {
    const res = await fetch(`${serverUrl}/Users/AuthenticateByName`, {
        method: 'POST',
        headers: { Authorization: AUTH, 'Content-Type': 'application/json' },
        body: JSON.stringify({ Username: user.name, Pw: user.password })
    });
    if (!res.ok) { throw new Error(`auth failed ${res.status}`); }
    const j = await res.json();
    return { token: j.AccessToken, userId: j.User.Id, header: `${AUTH}, Token="${j.AccessToken}"` };
}

export async function api(session, route, opts = {}) {
    const res = await fetch(`${serverUrl}${route}`, { ...opts, headers: { Authorization: session.header, 'Content-Type': 'application/json', ...(opts.headers || {}) } });
    const text = await res.text();
    let body = text;
    try { body = JSON.parse(text); } catch { /* not JSON */ }
    return { status: res.status, body };
}

export function stats(values) {
    const v = values.filter((x) => typeof x === 'number' && !Number.isNaN(x)).sort((a, b) => a - b);
    if (v.length === 0) { return { n: 0, median: null, p95: null }; }
    const q = (p) => {
        const idx = (v.length - 1) * p;
        const lo = Math.floor(idx), hi = Math.ceil(idx);
        return v[lo] + (v[hi] - v[lo]) * (idx - lo);
    };
    // Median: linear interpolation. p95: nearest rank, v[ceil(0.95 n) - 1] (equals the maximum when n <= 19).
    return { n: v.length, median: q(0.5), p95: v[Math.ceil(0.95 * v.length) - 1], min: v[0], max: v[v.length - 1] };
}

import { execFileSync } from 'node:child_process';

export const CONTAINER = process.env.JUX_CONTAINER || 'jellyux-test';

export function dockerStats() {
    const out = execFileSync('docker', ['stats', '--no-stream', '--format={{.Name}},{{.CPUPerc}},{{.MemUsage}}', CONTAINER], { encoding: 'utf8', timeout: 60000 }).trim();
    const [name, cpu, mem] = out.split(',');
    const used = mem.split('/')[0].trim();
    const m = used.match(/([\d.]+)\s*(KiB|MiB|GiB|B)/);
    const factor = { B: 1 / 1048576, KiB: 1 / 1024, MiB: 1, GiB: 1024 }[m[2]];
    return { raw: out, name, cpu, memMiB: Number(m[1]) * factor };
}

export function sleep(ms) { return new Promise((r) => setTimeout(r, ms)); }

export async function waitHealthy(timeoutMs = 300000) {
    const t0 = Date.now();
    while (Date.now() - t0 < timeoutMs) {
        try {
            const res = await fetch(`${serverUrl}/health`, { signal: AbortSignal.timeout(3000) });
            if (res.ok && (await res.text()).includes('Healthy')) { return Date.now(); }
        } catch { /* not up yet */ }
        await sleep(250);
    }
    throw new Error('server did not become healthy');
}

const MARKERS = [
    ['jellyfinVersion', /Main: Jellyfin version/],
    ['juxAssemblyLoaded', /Loaded assembly Jellyfin\.Plugin\.JuxHomepage/],
    ['juxPluginLoaded', /Loaded plugin: JellyUX Homepage/],
    ['juxIndexRegistered', /Registered index\.html transformation for JellyUX/],
    ['foundLoadSections', /Found loadSections chunk/],
    ['registeredLoadSections', /Registered loadSections chunk transformation/],
    ['foundHomeHtml', /Found home-html tab anchor/],
    ['registeredHomeHtml', /Registered home-html chunk transformation/],
    ['startupComplete', /Main: Startup complete/]
];

/** Restarts the container and returns wall-clock and log-derived startup timings (ms). */
export async function restartAndMeasure() {
    const tCmd = Date.now();
    execFileSync('docker', ['restart', CONTAINER], { timeout: 180000 });
    const tRestartReturned = Date.now();
    const tHealthy = await waitHealthy();
    const startedAtIso = execFileSync('docker', ['inspect', CONTAINER, '--format={{.State.StartedAt}}'], { encoding: 'utf8' }).trim();
    const startedAt = Date.parse(startedAtIso.slice(0, 23) + 'Z');
    // Wait for the "Startup complete" line (health can answer slightly before it).
    let logs = '';
    for (let i = 0; i < 120; i++) {
        logs = execFileSync('docker', ['logs', '-t', '--since', startedAtIso, CONTAINER], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
        if (/Main: Startup complete/.test(logs)) { break; }
        await sleep(500);
    }
    // docker logs -t splits stdout/stderr; Jellyfin writes to stdout. Parse RFC3339Nano prefixes.
    const lines = logs.split(/\r?\n/);
    const marks = {};
    let firstLine = null;
    for (const line of lines) {
        const m = line.match(/^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d+)Z\s(.*)$/);
        if (!m) { continue; }
        const t = Date.parse(m[1].slice(0, 23) + 'Z');
        if (t < startedAt) { continue; }
        if (firstLine === null) { firstLine = t; }
        for (const [k, re] of MARKERS) {
            if (marks[k] === undefined && re.test(m[2])) { marks[k] = t; }
        }
    }
    const rel = {};
    for (const [k] of MARKERS) { rel[k] = marks[k] !== undefined ? marks[k] - startedAt : null; }
    const startupLine = lines.find((l) => /Main: Startup complete/.test(l));
    return {
        tCmd, startedAtIso, restartCmdMs: tRestartReturned - tCmd, cmdToHealthyMs: tHealthy - tCmd,
        startedToFirstLogMs: firstLine - startedAt, startedToHealthyMs: tHealthy - startedAt, markersFromProcessStartMs: rel,
        jellyfinReportedStartup: startupLine ? startupLine.replace(/^.*Startup complete /, '').trim() : null,
        loadSectionsLine: (lines.find((l) => /Found loadSections chunk/.test(l)) || '').replace(/^\S+\s/, '')
    };
}
