// PSRV probe: cost of the Likes filter. JUX Watchlist/Ids and Watchlist/Items against native /Items
// queries with the same filter (test2, empty watchlist), interleaved, --reps passes, curl time_total.
//   node bench/perf-server-watchlist.mjs --reps 7
import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { serverUrl, rawDir, stats } from './perf-bench-common.mjs';
import { authUnique } from './perf-server-auth.mjs';

const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? process.argv[i + 1] : d; };
const reps = Number(arg('reps', 7));
const u = await authUnique('user');
const id = u.userId;
const routes = [
    ['jux Watchlist/Ids', `/JuxHomepage/Watchlist/Ids?userId=${id}`],
    ['jux Watchlist/Items (total on)', `/JuxHomepage/Watchlist/Items?userId=${id}&startIndex=0&limit=50`],
    ['native Likes ids-like (no total, no fields, no images)', `/Items?userId=${id}&Recursive=true&IncludeItemTypes=Movie,Series&Filters=Likes&EnableTotalRecordCount=false&EnableImages=false&Fields=`],
    ['native Likes page (total on)', `/Items?userId=${id}&Recursive=true&IncludeItemTypes=Movie,Series&Filters=Likes&SortBy=DateCreated&SortOrder=Descending&Limit=50&Fields=PrimaryImageAspectRatio,DateCreated`],
    ['native no filter Limit=1 (baseline)', `/Items?userId=${id}&Recursive=true&IncludeItemTypes=Movie,Series&Limit=1&EnableTotalRecordCount=false&EnableImages=false&Fields=`],
];
function curl(route) {
    const out = execFileSync('curl', ['-s', '-o', os.devNull, '-H', `Authorization: ${u.header}`, '-w', '%{http_code},%{time_total}', `${serverUrl}${route}`], { encoding: 'utf8', timeout: 120000 });
    const [code, total] = out.trim().split(',');
    if (Number(code) !== 200) { throw new Error(`${code} on ${route}`); }
    return Number(total) * 1000;
}
const v = routes.map(() => []);
const rows = ['rep,label,ms'];
for (let r = 0; r < reps; r++) {
    routes.forEach(([label, route], i) => { const ms = curl(route); v[i].push(ms); rows.push([r, `"${label}"`, ms.toFixed(1)].join(',')); });
}
routes.forEach(([label], i) => { const s = stats(v[i]); console.log(label.padEnd(58), 'median', s.median.toFixed(0), 'p95', s.p95.toFixed(0)); });
writeFileSync(path.join(rawDir, 'perf-server-watchlist.csv'), rows.join('\n') + '\n');
await u.logout();
