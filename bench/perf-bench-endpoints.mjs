// Server-side latency of the JellyUX GET endpoints the client uses, with curl -w.
// Each cycle restarts the container (cold = first call of each endpoint after the restart, in the
// fixed order below, no browser attached), then runs N warm passes.
//   node bench/perf-bench-endpoints.mjs --profile std --cycles 5 --warm 10
import { execFileSync } from 'node:child_process';
import { appendFileSync, writeFileSync, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { readCredentials, authenticate, api, serverUrl, rawDir, restartAndMeasure, dockerStats, sleep } from './perf-bench-common.mjs';

const arg = (name, def) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : def; };
const profile = arg('profile', 'std');
const cycles = Number(arg('cycles', 5));
const warm = Number(arg('warm', 10));
const settleMs = Number(arg('settle', 30000));
const csv = path.join(rawDir, `perf-bench-endpoints-${profile}.csv`);
const startupFile = path.join(rawDir, `perf-bench-startup-endpoints-${profile}.json`);

const creds = readCredentials();
const user = await authenticate(creds.user);
const admin = await authenticate(creds.admin);
const U = user.userId;
const A = admin.userId;
const V = '99f3c54875cc';

// A section is requested with the instance data its layout row carries now (a scored genre, actor or
// title changes with the history), read once before the first restart, so every request hits a row of
// the layout. The cold measures are unaffected: each cycle restarts the server first.
const layout = (await api(user, `/JuxHomepage/Sections?userId=${U}&page=0&lang=en-us`)).body.Sections;
function section(name, sectionKey, labelWithData = false) {
    const row = layout.find((s) => s.SectionKey === sectionKey);
    if (!row) {
        throw new Error(`no layout row ${sectionKey}`);
    }
    const query = new URLSearchParams({ userId: U, sectionKey, startIndex: '0', limit: String(row.MaxItems) });
    if (row.AdditionalData) {
        query.set('additionalData', row.AdditionalData);
    }
    return [`Section ${name}${labelWithData ? ` (${row.AdditionalData})` : ''}`, user, `/JuxHomepage/Section/${row.WidgetType}?${query}`];
}

// [label, session, route]; client query strings copied from the browser capture (explore run).
const ENDPOINTS = [
    ['asset jux-homepage.js', user, `/JuxHomepage/jux-homepage.js?v=${V}`],
    ['asset jux-i18n.js', user, `/JuxHomepage/jux-i18n.js?v=${V}`],
    ['Watchlist/Ids', user, `/JuxHomepage/Watchlist/Ids?userId=${U}`],
    ['Sections', user, `/JuxHomepage/Sections?userId=${U}&page=0&lang=en-us`],
    ['native Items/Latest movies', user, `/Items/Latest?userId=${U}&startIndex=0&limit=20&fields=PrimaryImageAspectRatio%2CDateCreated&imageTypeLimit=1&enableImageTypes=Primary%2CThumb%2CBackdrop&enableTotalRecordCount=false&includeItemTypes=Movie`],
    ['native Items/Latest episodes x200', user, `/Items/Latest?userId=${U}&startIndex=0&limit=200&fields=PrimaryImageAspectRatio%2CDateCreated&imageTypeLimit=1&enableImageTypes=Primary%2CThumb%2CBackdrop&enableTotalRecordCount=false&includeItemTypes=Episode&groupItems=false`],
    section('favorite-genre', 'jux.personalized.favorite-genre#1', true),
    section('trending-movies', 'jux.connected.trending-movies#1'),
    section('favorite-genre', 'jux.personalized.favorite-genre#2', true),
    section('trending-shows', 'jux.connected.trending-shows#1'),
    section('rewards', '67361e0a-85f8-4547-8291-f9cbd62a9260'),
    section('favorite-actor', 'jux.personalized.favorite-actor#1'),
    section('because-you-watched', 'jux.personalized.because-you-watched#1'),
    section('admin.genre', 'jux.admin.genre#1', true),
    ['Watchlist/Items', user, `/JuxHomepage/Watchlist/Items?userId=${U}&sortBy=DateCreated&sortOrder=Descending&includeItemTypes=All&startIndex=0&limit=50`],
    ['SeriesProgress/Items', user, `/JuxHomepage/SeriesProgress/Items?userId=${U}&sortBy=LastPlayed&sortOrder=Descending&startIndex=0&limit=50`],
    ['MovieHistory/Items', user, `/JuxHomepage/MovieHistory/Items?userId=${U}&sortBy=LastPlayed&sortOrder=Descending&startIndex=0&limit=50`],
    ['Statistics', user, `/JuxHomepage/Statistics?userId=${U}`],
    ['Collections/IncludedIn', user, '/JuxHomepage/Collections/IncludedIn/938f0d3f9973e6ea1014a80a0212dc7c'],
    ['Localizations', user, '/JuxHomepage/Localizations?lang=en'],
    ['UserSettings', user, `/JuxHomepage/UserSettings?userId=${U}&lang=en`],
    ['admin Admin/Warnings', admin, '/JuxHomepage/Admin/Warnings'],
    ['admin Admin/Catalog', admin, '/JuxHomepage/Admin/Catalog'],
    ['admin Watchlist/Items (4 items)', admin, `/JuxHomepage/Watchlist/Items?userId=${A}&sortBy=DateCreated&sortOrder=Descending&includeItemTypes=All&startIndex=0&limit=50`],
    ['admin SeriesProgress/Items (2)', admin, `/JuxHomepage/SeriesProgress/Items?userId=${A}&sortBy=LastPlayed&sortOrder=Descending&startIndex=0&limit=50`],
    ['admin MovieHistory/Items (12)', admin, `/JuxHomepage/MovieHistory/Items?userId=${A}&sortBy=LastPlayed&sortOrder=Descending&startIndex=0&limit=50`],
    ['admin Statistics', admin, `/JuxHomepage/Statistics?userId=${A}`]
];

function curl(session, route) {
    const out = execFileSync('curl', ['-s', '-o', os.devNull, '-H', `Authorization: ${session.header}`, '-w', '%{http_code},%{time_starttransfer},%{time_total},%{size_download}', `${serverUrl}${route}`], { encoding: 'utf8', timeout: 120000 });
    const [code, ttfb, total, size] = out.trim().split(',');
    return { code: Number(code), ttfbMs: Number(ttfb) * 1000, totalMs: Number(total) * 1000, size: Number(size) };
}

if (!existsSync(csv)) { writeFileSync(csv, 'profile,cycle,mode,pass,endpoint,http,ttfb_ms,total_ms,size_bytes\n'); }
const startups = existsSync(startupFile) ? JSON.parse(await (await import('node:fs/promises')).readFile(startupFile, 'utf8')) : [];
for (let c = 1; c <= cycles; c++) {
    const st = await restartAndMeasure();
    await sleep(settleMs);
    st.memRest = dockerStats();
    startups.push({ profile, source: 'endpoints', cycle: c, ...st });
    writeFileSync(startupFile, JSON.stringify(startups, null, 1));
    const rows = [];
    for (const [label, s, route] of ENDPOINTS) {
        const r = curl(s, route);
        rows.push([profile, c, 'cold', 0, `"${label}"`, r.code, r.ttfbMs.toFixed(1), r.totalMs.toFixed(1), r.size].join(','));
    }
    for (let p = 1; p <= warm; p++) {
        for (const [label, s, route] of ENDPOINTS) {
            const r = curl(s, route);
            rows.push([profile, c, 'warm', p, `"${label}"`, r.code, r.ttfbMs.toFixed(1), r.totalMs.toFixed(1), r.size].join(','));
        }
    }
    appendFileSync(csv, rows.join('\n') + '\n');
    console.log(new Date().toISOString(), profile, 'cycle', c, 'done; startup', st.cmdToHealthyMs, 'ms; mem', st.memRest.memMiB);
}
