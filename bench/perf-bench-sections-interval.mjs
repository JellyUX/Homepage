// Latency of GET /JuxHomepage/Sections (test2) as a function of the delay since the previous call.
// For each gap, the endpoint is called once to prime it, then called again after the gap; repeated
// `reps` times per gap. No container restart: this isolates the warm-state behaviour.
//   node bench/perf-bench-sections-interval.mjs --gaps 0,5,15,30,45,60,90 --reps 5 --profile std
import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { readCredentials, authenticate, serverUrl, rawDir, sleep } from './perf-bench-common.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';

const series = await startSeries({ label: 'perf-bench-sections-interval' });

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const gaps = arg('gaps', '0,5,15,30,45,60,90').split(',').map(Number);
const reps = Number(arg('reps', 5));
const profile = arg('profile', 'std');
const user = await authenticate(readCredentials().user);
const route = `/JuxHomepage/Sections?userId=${user.userId}&page=0&lang=en-us`;

function curl() {
    const out = execFileSync('curl', ['-s', '-o', os.devNull, '-H', `Authorization: ${user.header}`, '-w', '%{http_code},%{time_total}', `${serverUrl}${route}`], { encoding: 'utf8', timeout: 120000 });
    const [code, total] = out.trim().split(',');
    return { code: Number(code), totalMs: Number(total) * 1000 };
}

const rows = ['profile,gap_s,rep,prime_ms,http,total_ms'];
for (const gap of gaps) {
    for (let r = 1; r <= reps; r++) {
        const prime = curl();
        await sleep(gap * 1000);
        const x = curl();
        rows.push([profile, gap, r, prime.totalMs.toFixed(1), x.code, x.totalMs.toFixed(1)].join(','));
        console.log(new Date().toISOString(), 'gap', gap, 'rep', r, 'prime', prime.totalMs.toFixed(0), 'after gap', x.totalMs.toFixed(0));
    }
}
writeFileSync(path.join(rawDir, `perf-bench-sections-interval-${profile}.csv`), rows.join('\n') + '\n');
await endSeries(series);
