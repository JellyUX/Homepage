// PSRV probe: why is GET /JuxHomepage/Sections bimodal?
// Part A: poll Sections (test2, lang=fr: its own SessionCache key, distinct from the browser's "en")
//         every --step seconds for --duration seconds. Under a sliding TTL a steady poll never misses;
//         under an absolute TTL (SessionCache.TryGet: now - CreatedAt < ttl) a miss recurs every ~TTL.
// Part B: time each of the 16 enabled rows' probe, approximated by GET Section/{type}?limit=MinItems
//         (same widget GetItemsAsync, same Limit as WidgetLayoutResolver.FetchInstanceDescriptor),
//         --reps times, to compare the sum and the max of the probes with the slow Sections time.
//   node bench/perf-server-sections-ttl.mjs --step 10 --duration 260 --reps 5
import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { serverUrl, rawDir, sleep, stats } from './perf-bench-common.mjs';
import { authUnique } from './perf-server-auth.mjs';
import { startSeries, endSeries } from './bench-fingerprint.mjs';

const series = await startSeries({ label: 'perf-server-sections-ttl' });

const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const step = Number(arg('step', 10)), duration = Number(arg('duration', 260)), reps = Number(arg('reps', 5));
const skipA = process.argv.includes('--skip-poll');
const u = await authUnique('user');

function curl(route) {
    const out = execFileSync('curl', ['-s', '-o', os.devNull, '-H', `Authorization: ${u.header}`, '-w', '%{http_code},%{time_total}', `${serverUrl}${route}`], { encoding: 'utf8', timeout: 120000 });
    const [code, total] = out.trim().split(',');
    if (Number(code) === 401) { throw new Error('401: token revoked'); }
    return { code: Number(code), ms: Number(total) * 1000 };
}

const rows = ['part,t_iso,t_rel_s,label,http,ms'];
const tStart = Date.now();
if (!skipA) {
    for (let t = 0; t <= duration; t += step) {
        const target = tStart + t * 1000;
        if (Date.now() < target) await sleep(target - Date.now());
        const x = curl(`/JuxHomepage/Sections?userId=${u.userId}&page=0&lang=fr`);
        const rel = ((Date.now() - tStart) / 1000).toFixed(1);
        rows.push(['A', new Date().toISOString(), rel, 'Sections-fr', x.code, x.ms.toFixed(1)].join(','));
        console.log('A', rel, x.code, x.ms.toFixed(0));
    }
}

const probes = [
    ['jux.native.continue-watching', null, 'jux.native.continue-watching#1', 1],
    ['jux.native.next-up', null, 'jux.native.next-up#1', 1],
    ['jux.native.recently-added-movies', null, 'jux.native.recently-added-movies#1', 4],
    ['jux.native.recently-added-episodes', null, 'jux.native.recently-added-episodes#1', 4],
    ['jux.personalized.favorite-genre', 'Action', 'jux.personalized.favorite-genre#1', 1],
    ['jux.native.watchlist', null, 'jux.native.watchlist#1', 1],
    ['jux.connected.trending-movies', null, 'jux.connected.trending-movies#1', 1],
    ['jux.personalized.favorite-genre', 'Drame', 'jux.personalized.favorite-genre#2', 1],
    ['jux.connected.airing-today', null, 'jux.connected.airing-today#1', 1],
    ['jux.connected.trending-shows', null, 'jux.connected.trending-shows#1', 1],
    ['jux.connected.rewards', '77e6cab7-f5c4-41e7-b64a-10766f3a0e4e', '77e6cab7-f5c4-41e7-b64a-10766f3a0e4e', 1],
    ['jux.connected.rewards', '67361e0a-85f8-4547-8291-f9cbd62a9260', '67361e0a-85f8-4547-8291-f9cbd62a9260', 1],
    ['jux.personalized.favorite-actor', 'Amy Ryan', 'jux.personalized.favorite-actor#1', 1],
    ['jux.personalized.because-you-watched', 'e9ac0ed8-ee98-5ec1-9293-8f61269e89d1', 'jux.personalized.because-you-watched#1', 4],
    ['jux.admin.genre', 'Aventure', 'jux.admin.genre#1', 1],
    ['jux.connected.seasonal', '82d49141-0577-4dc4-b7e9-8c3160340f97', '82d49141-0577-4dc4-b7e9-8c3160340f97', 1],
];
const med = [];
for (const [type, add, key, min] of probes) {
    const q = new URLSearchParams({ userId: u.userId, sectionKey: key, startIndex: '0', limit: String(min) });
    if (add) q.set('additionalData', add);
    const v = [];
    for (let r = 0; r < reps; r++) {
        const x = curl(`/JuxHomepage/Section/${type}?${q}`);
        v.push(x.ms);
        rows.push(['B', new Date().toISOString(), ((Date.now() - tStart) / 1000).toFixed(1), key, x.code, x.ms.toFixed(1)].join(','));
    }
    const s = stats(v);
    med.push(s.median);
    console.log('B', key, 'median', s.median.toFixed(0), 'max', s.max.toFixed(0));
}
const sum = med.reduce((a, b) => a + b, 0);
console.log('B sum of medians', sum.toFixed(0), 'max median', Math.max(...med).toFixed(0));
rows.push(['B-sum', new Date().toISOString(), '', 'sum-of-medians', '', sum.toFixed(1)].join(','));
rows.push(['B-max', new Date().toISOString(), '', 'max-of-medians', '', Math.max(...med).toFixed(1)].join(','));
writeFileSync(path.join(rawDir, `perf-server-sections-ttl${skipA ? '-probes' : ''}.csv`), rows.join('\n') + '\n');
await u.logout();
await endSeries(series);
