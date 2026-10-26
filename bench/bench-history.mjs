// Bench user history (PROC-01.e) on a disposable copy of the big set, never on a bench whose state matters:
// the bench user (second login of identifiants.txt) gets N played movies (dates spread over 2 years), W liked
// unplayed movies (the JellyUX watchlist), 10 in-progress movies, 10 favourites and the first 3 episodes of 10
// series played. The choice is deterministic (seeded shuffle), so a template rebuilt from the same library gets
// the same history. The reference template holds 500 played, 100 liked, 10 in progress (bench-fingerprint.mjs
// "user"); PROC-01.e asks for 500 to 2 000 played and 50 to 200 liked. Ported from the audit V5 bench-history.mjs.
//   JUX_URL=http://localhost:8111 node bench/bench-history.mjs --played 500 --watchlist 100 [--add]
// --add: only plays more movies (from the unplayed ones) until N are played, nothing else is touched.
import { readCredentials, authenticate, api, serverUrl } from './perf-bench-common.mjs';

// 8096 and 8097 are the production-like and KefinTweaks benches of this machine.
if (/:(8096|8097)$/.test(serverUrl)) { throw new Error('disposable benches only'); }
const arg = (n, d) => { const i = process.argv.indexOf(`--${n}`); return i > 0 ? Number(process.argv[i + 1]) : d; };
const N = arg('played', 500), W = arg('watchlist', 100), addOnly = process.argv.includes('--add');
let seed = 42;
const rnd = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
const shuffle = (a) => { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };

const s = await authenticate(readCredentials().user);
const out = { at: new Date().toISOString(), server: serverUrl };
try {
    const movies = shuffle((await api(s, `/Items?userId=${s.userId}&IncludeItemTypes=Movie&Recursive=true&SortBy=SortName&Fields=UserData`)).body.Items);
    const day = 86400000, now = Date.now();
    const playedNow = movies.filter((m) => m.UserData && m.UserData.Played);
    const toPlay = addOnly ? movies.filter((m) => !(m.UserData && m.UserData.Played)).slice(0, Math.max(0, N - playedNow.length)) : movies.slice(0, N);
    let k = 0;
    for (const m of toPlay) {
        const date = new Date(now - Math.floor(((k++ + 1) / toPlay.length) * 730 * day)).toISOString();
        await api(s, `/UserPlayedItems/${m.Id}?datePlayed=${encodeURIComponent(date)}`, { method: 'POST' });
    }
    out.playedAdded = toPlay.length;
    if (!addOnly) {
        const rest = movies.slice(N);
        for (const m of rest.slice(0, W)) { await api(s, `/UserItems/${m.Id}/Rating?likes=true`, { method: 'POST' }); }
        out.liked = Math.min(W, rest.length);
        for (const m of rest.slice(W, W + 10)) {
            const ticks = Math.floor((m.RunTimeTicks || 6e10) * 0.4);
            await api(s, `/UserItems/${m.Id}/UserData`, { method: 'POST', body: JSON.stringify({ PlaybackPositionTicks: ticks, LastPlayedDate: new Date(now - 3 * day).toISOString() }) });
        }
        out.inProgress = 10;
        for (const m of movies.slice(0, 10)) { await api(s, `/UserFavoriteItems/${m.Id}`, { method: 'POST' }); }
        out.favourites = 10;
        const series = shuffle((await api(s, `/Items?userId=${s.userId}&IncludeItemTypes=Series&Recursive=true&SortBy=SortName`)).body.Items).slice(0, 10);
        let eps = 0;
        for (const se of series) {
            const e = (await api(s, `/Shows/${se.Id}/Episodes?userId=${s.userId}&limit=3`)).body.Items || [];
            for (const ep of e) { await api(s, `/UserPlayedItems/${ep.Id}?datePlayed=${encodeURIComponent(new Date(now - 10 * day).toISOString())}`, { method: 'POST' }); eps++; }
        }
        out.episodesPlayed = eps;
    }
    const count = async (q) => (await api(s, `/Items?userId=${s.userId}&Recursive=true&limit=0&enableTotalRecordCount=true&${q}`)).body.TotalRecordCount;
    out.totals = { playedMovies: await count('IncludeItemTypes=Movie&Filters=IsPlayed'), likedMovies: await count('IncludeItemTypes=Movie&Filters=Likes'), resumable: await count('Filters=IsResumable'), favourites: await count('Filters=IsFavorite') };
} finally {
    await api(s, '/Sessions/Logout', { method: 'POST' });
}
console.log(JSON.stringify(out));
