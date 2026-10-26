// Runs each JellyUX scheduled task N times through the API (admin) and records the server-side
// duration from LastExecutionResult, plus the JellyUX log lines written during each run.
//   node bench/perf-bench-tasks.mjs --runs 5
import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import path from 'node:path';
import { readCredentials, authenticate, api, rawDir, sleep, CONTAINER } from './perf-bench-common.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';

const series = await startSeries({ label: 'perf-bench-tasks' });

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const runs = Number(arg('runs', 5));
const profile = arg('profile', 'std');
const admin = await authenticate(readCredentials().admin);
const tasks = (await api(admin, '/ScheduledTasks')).body.filter((t) => t.Category === 'JellyUX Homepage');
const out = [];
for (const t of tasks) {
    for (let r = 1; r <= runs; r++) {
        const before = (await api(admin, `/ScheduledTasks/${t.Id}`)).body.LastExecutionResult;
        const sinceIso = new Date(Date.now() - 2000).toISOString();
        const tReq = Date.now();
        const post = await api(admin, `/ScheduledTasks/Running/${t.Id}`, { method: 'POST' });
        let after = null;
        for (let i = 0; i < 1200; i++) {
            await sleep(500);
            const cur = (await api(admin, `/ScheduledTasks/${t.Id}`)).body;
            if (cur.State === 'Idle' && cur.LastExecutionResult && (!before || cur.LastExecutionResult.StartTimeUtc !== before.StartTimeUtc)) { after = cur.LastExecutionResult; break; }
        }
        const logs = execFileSync('docker', ['logs', '-t', '--since', sinceIso, CONTAINER], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] })
            .split(/\r?\n/).filter((l) => /JuxHomepage|TaskManager: JellyUX/.test(l)).map((l) => l.slice(0, 400));
        const durationMs = after ? Date.parse(after.EndTimeUtc) - Date.parse(after.StartTimeUtc) : null;
        out.push({ profile, task: t.Name, key: t.Key, run: r, postStatus: post.status, status: after && after.Status, startUtc: after && after.StartTimeUtc, endUtc: after && after.EndTimeUtc, durationMs, wallMs: Date.now() - tReq, logs });
        console.log(t.Name, r, durationMs, after && after.Status, 'log lines', logs.length);
        await sleep(2000);
    }
}
writeFileSync(path.join(rawDir, `perf-bench-tasks-${profile}.json`), JSON.stringify(out, null, 1));
await endSeries(series);
