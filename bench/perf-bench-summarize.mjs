// Aggregates the perf-bench raw files into flat CSVs and prints median / p95 tables (markdown).
//   node bench/perf-bench-summarize.mjs
import { readFileSync, existsSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { rawDir, stats } from './perf-bench-common.mjs';

const f1 = (x) => (x === null || x === undefined ? 'n/a' : (Math.round(x * 10) / 10).toLocaleString('fr-FR'));
const f0 = (x) => (x === null || x === undefined ? 'n/a' : Math.round(x).toLocaleString('fr-FR'));
const kib = (x) => (x === null || x === undefined ? 'n/a' : (Math.round(x / 1024 * 10) / 10).toLocaleString('fr-FR'));
const load = (name) => { const p = path.join(rawDir, name); return existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')) : null; };

function flatHome(run) {
    const h = run.home;
    const list = h.net.list;
    const local = list.filter((r) => !r.url.startsWith('http'));
    const ext = list.filter((r) => r.url.startsWith('http'));
    const v = {
        'home.firstSectionMs': h.firstSectionMs, 'home.allSectionsMs': h.allSectionsMs,
        'home.sectionsWithCards': h.sectionsWithCards,
        'home.requests': h.net.requests, 'home.requestsLocal': local.length, 'home.requestsExternal': ext.length,
        'home.requestsLocalNetwork': local.filter((r) => !r.cache).length,
        'home.bytes': h.net.bytes, 'home.bytesLocal': local.reduce((a, r) => a + r.bytes, 0), 'home.bytesExternal': ext.reduce((a, r) => a + r.bytes, 0),
        'home.juxRequests': h.net.juxRequests, 'home.juxRequestsNetwork': list.filter((r) => r.url.includes('/JuxHomepage/') && !r.cache).length, 'home.juxBytes': h.net.juxBytes,
        'home.juxApiRequests': list.filter((r) => /\/JuxHomepage\/[A-Z]/.test(r.url)).length,
        'home.scriptDurationMs': h.scriptDurationMs, 'home.taskDurationMs': h.taskDurationMs, 'home.layoutDurationMs': h.layoutDurationMs,
        'home.longtaskCount': h.longtaskCount, 'home.longtaskTotalMs': h.longtaskTotalMs, 'home.longtaskMaxMs': h.longtaskMaxMs,
        'home.jsHeapUsedMiB': h.jsHeapUsedMiB
    };
    if (run.back) {
        v['back.firstSectionMs'] = run.back.firstSectionMs; v['back.allSectionsMs'] = run.back.allSectionsMs;
        v['back.requestsLocal'] = run.back.net.list.filter((r) => !r.url.startsWith('http')).length;
        v['back.juxRequests'] = run.back.net.juxRequests; v['back.rebuilt'] = run.back.rebuilt ? 1 : 0;
    }
    for (const [name, t] of Object.entries(run.tabs || {})) {
        v[`tab.${name}.renderedMs`] = t.renderedMs;
        v[`tab.${name}.requestsLocal`] = t.net.list.filter((r) => !r.url.startsWith('http')).length;
        v[`tab.${name}.juxRequests`] = t.net.juxRequests;
        v[`tab.${name}.juxBytes`] = t.net.juxBytes;
    }
    return v;
}

const rows = [];
const browserSets = [];
for (const [profile, viewport] of [['std', 'desktop'], ['std', 'mobile'], ['lowend', 'desktop'], ['lowend', 'mobile']]) {
    const file = `perf-bench-browser-${profile}-${viewport}.json`;
    const data = load(file);
    if (!data) { continue; }
    const set = { profile, viewport, file, cold: [], warm: [], mem: { rest: [], afterHome: [], afterTabs: [] }, runs: [] };
    for (const c of data.cycles) {
        set.mem.rest.push(c.memRest && c.memRest.memMiB);
        if (c.cold) {
            set.mem.afterHome.push(c.cold.afterHome && c.cold.afterHome.mem.memMiB);
            set.mem.afterTabs.push(c.memAfterTabs && c.memAfterTabs.memMiB);
            const v = flatHome(c.cold); set.cold.push(v); set.runs.push({ mode: 'cold', cycle: c.cycle, run: c.cold });
            for (const [k, x] of Object.entries(v)) { rows.push([profile, viewport, 'cold', c.cycle, 0, k, x]); }
        }
        (c.warm || []).forEach((w, i) => {
            if (w.error) { rows.push([profile, viewport, 'warm', c.cycle, i + 1, 'error', JSON.stringify(w.error)]); return; }
            const v = flatHome(w); set.warm.push(v); set.runs.push({ mode: 'warm', cycle: c.cycle, run: w });
            for (const [k, x] of Object.entries(v)) { rows.push([profile, viewport, 'warm', c.cycle, i + 1, k, x]); }
        });
    }
    browserSets.push(set);
}
writeFileSync(path.join(rawDir, 'perf-bench-browser-flat.csv'), 'profile,viewport,mode,cycle,run,indicator,value\n' + rows.map((r) => r.join(',')).join('\n') + '\n');

const fmtFor = (k) => (/bytes|Bytes/.test(k) ? kib : /Ms$|MiB$/.test(k) ? f0 : f1);
for (const set of browserSets) {
    console.log(`\n### Navigateur ${set.profile} ${set.viewport} (${set.file})\n`);
    console.log('| Indicateur | froid médiane | froid p95 | chaud médiane | chaud p95 | n froid / chaud |');
    console.log('|---|---|---|---|---|---|');
    const keys = [...new Set([...set.cold, ...set.warm].flatMap((v) => Object.keys(v)))];
    for (const k of keys) {
        const c = stats(set.cold.map((v) => v[k])); const w = stats(set.warm.map((v) => v[k]));
        const f = fmtFor(k);
        console.log(`| ${k}${/bytes|Bytes/.test(k) ? ' (KiB)' : ''} | ${f(c.median)} | ${f(c.p95)} | ${f(w.median)} | ${f(w.p95)} | ${c.n} / ${w.n} |`);
    }
    for (const [k, arr] of Object.entries(set.mem)) { const s = stats(arr); console.log(`| mem.${k} (MiB) | ${f1(s.median)} | ${f1(s.p95)} | | | ${s.n} |`); }

    // Duplicated URLs (same method + URL more than once) during the home load, and across the session.
    const dupHome = new Map(); const dupSession = new Map();
    for (const { mode, run } of set.runs) {
        for (const d of run.home.net.duplicates) { if (!d.url.includes('http')) { const k = `${mode} ${d.url}`; const e = dupHome.get(k) || { runs: 0, n: [] }; e.runs++; e.n.push(d.n); dupHome.set(k, e); } }
        const all = [...run.home.net.list, ...(run.back ? run.back.net.list : []), ...Object.values(run.tabs || {}).flatMap((t) => t.net.list)].filter((r) => !r.url.startsWith('http') && r.url.includes('/JuxHomepage/'));
        const m = new Map(); for (const r of all) { const k = `${r.m} ${r.url}`; m.set(k, (m.get(k) || 0) + 1); }
        for (const [u, n] of m) { if (n > 1) { const k = `${mode} ${u}`; const e = dupSession.get(k) || { runs: 0, n: [] }; e.runs++; e.n.push(n); dupSession.set(k, e); } }
    }
    console.log('\nDoublons (même méthode + URL locale) pendant le chargement de l\'accueil :');
    for (const [k, e] of dupHome) { console.log(`- ${k.slice(0, 200)} : ${e.runs} passes, occurrences ${[...new Set(e.n)].join('/')}`); }
    console.log('\nDoublons /JuxHomepage/* sur la session (accueil + retour + 4 onglets) :');
    for (const [k, e] of dupSession) { console.log(`- ${k.slice(0, 200)} : ${e.runs} passes, occurrences ${[...new Set(e.n)].join('/')}`); }

    // Longest long task and longest long animation frame with its scripts.
    let lt = null; let lf = null;
    for (const { mode, cycle, run } of set.runs) {
        for (const t of run.home.longtasks) { if (!lt || t.dur > lt.dur) { lt = { ...t, mode, cycle }; } }
        for (const f of run.home.loaf) { if (!lf || f.dur > lf.dur) { lf = { ...f, mode, cycle }; } }
    }
    console.log('\nPlus longue long task :', JSON.stringify(lt));
    console.log('Plus long long-animation-frame :', JSON.stringify(lf && { ...lf, scripts: lf.scripts.sort((a, b) => b.dur - a.dur).slice(0, 5) }));
    // Script time attributed to sources over all long animation frames (> 50 ms) of the home load.
    const bySrc = new Map(); let frames = 0;
    for (const { run } of set.runs) { for (const f of run.home.loaf) { frames++; for (const s of f.scripts) { const k = (s.src || '(inline/none)').replace(/\?.*$/, '').replace(/^http:\/\/localhost:8096/, ''); bySrc.set(k, (bySrc.get(k) || 0) + s.dur); } } }
    console.log(`Temps de script attribué dans les LoAF > 50 ms (somme sur ${set.runs.length} passes, ${frames} frames), top 8 :`);
    [...bySrc.entries()].sort((a, b) => b[1] - a[1]).slice(0, 8).forEach(([k, d]) => console.log(`- ${k} : ${f0(d)} ms`));
}

// Endpoints.
for (const profile of ['std', 'lowend']) {
    const p = path.join(rawDir, `perf-bench-endpoints-${profile}.csv`);
    if (!existsSync(p)) { continue; }
    const lines = readFileSync(p, 'utf8').trim().split(/\r?\n/).slice(1);
    const m = new Map(); const order = [];
    for (const l of lines) {
        const mm = l.match(/^([^,]+),(\d+),(cold|warm),(\d+),"([^"]+)",(\d+),([\d.]+),([\d.]+),(\d+)$/);
        if (!mm) { continue; }
        const [, , , mode, , ep, code, ttfb, total, size] = mm;
        if (!m.has(ep)) { m.set(ep, { cold: [], coldT: [], warm: [], warmT: [], size: Number(size), codes: new Set() }); order.push(ep); }
        const e = m.get(ep); e.codes.add(code);
        if (mode === 'cold') { e.cold.push(Number(total)); e.coldT.push(Number(ttfb)); } else { e.warm.push(Number(total)); e.warmT.push(Number(ttfb)); }
    }
    console.log(`\n### Endpoints ${profile} (perf-bench-endpoints-${profile}.csv), time_total ms (TTFB entre parenthèses)\n`);
    console.log('| Endpoint | froid médiane | froid p95 | chaud médiane | chaud p95 | n froid / chaud | octets | HTTP |');
    console.log('|---|---|---|---|---|---|---|---|');
    for (const ep of order) {
        const e = m.get(ep); const c = stats(e.cold); const w = stats(e.warm); const ct = stats(e.coldT); const wt = stats(e.warmT);
        console.log(`| ${ep} | ${f1(c.median)} (${f1(ct.median)}) | ${f1(c.p95)} | ${f1(w.median)} (${f1(wt.median)}) | ${f1(w.p95)} | ${c.n} / ${w.n} | ${e.size} | ${[...e.codes].join('/')} |`);
    }
}

// Startup and memory at rest.
for (const profile of ['std', 'lowend']) {
    const recs = [];
    const add = (r, source) => { if (r) { recs.push({ ...r, source }); } };
    const b = load(`perf-bench-browser-${profile}-desktop.json`); if (b) { b.cycles.forEach((c) => add({ ...c.startup, memRest: c.memRest }, 'browser-desktop')); }
    const bm = load(`perf-bench-browser-${profile}-mobile.json`); if (bm) { bm.cycles.forEach((c) => add({ ...c.startup, memRest: c.memRest }, 'browser-mobile')); }
    const e = load(`perf-bench-startup-endpoints-${profile}.json`); if (e) { e.forEach((r) => add(r, 'endpoints')); }
    if (recs.length === 0) { continue; }
    console.log(`\n### Démarrage ${profile} (n = ${recs.length} redémarrages ; ms depuis State.StartedAt du conteneur)\n`);
    console.log('| Jalon | médiane | p95 | min | max | n |');
    console.log('|---|---|---|---|---|---|');
    const keys = Object.keys(recs[0].markersFromProcessStartMs || {});
    for (const k of keys) { const s = stats(recs.map((r) => r.markersFromProcessStartMs[k])); console.log(`| ${k} | ${f0(s.median)} | ${f0(s.p95)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} |`); }
    for (const k of ['startedToHealthyMs', 'cmdToHealthyMs']) { const s = stats(recs.map((r) => r[k])); console.log(`| ${k} | ${f0(s.median)} | ${f0(s.p95)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} |`); }
    const s = stats(recs.map((r) => r.memRest && r.memRest.memMiB)); console.log(`| mémoire au repos (MiB, 30 s après /health) | ${f1(s.median)} | ${f1(s.p95)} | ${f1(s.min)} | ${f1(s.max)} | ${s.n} |`);
    const d = stats(recs.map((r) => r.markersFromProcessStartMs.registeredHomeHtml - r.markersFromProcessStartMs.juxIndexRegistered));
    console.log(`| durée enregistrement patchs JellyUX (index.html -> home-html) | ${f0(d.median)} | ${f0(d.p95)} | ${f0(d.min)} | ${f0(d.max)} | ${d.n} |`);
}

for (const profile of ['std', 'lowend']) {
    const tg = load(`perf-bench-plugin-toggle-${profile}.json`);
    if (!tg) { continue; }
    const s = stats(tg.disabled.map((r) => r.memRest.memMiB));
    const h = stats(tg.disabled.map((r) => r.startedToHealthyMs));
    const sc = stats(tg.disabled.map((r) => r.markersFromProcessStartMs.startupComplete));
    console.log(`
### JellyUX désactivé ${profile} (perf-bench-plugin-toggle-${profile}.json)
- mémoire au repos : médiane ${f1(s.median)} MiB, p95 ${f1(s.p95)}, n=${s.n}
- StartedAt -> /health : médiane ${f0(h.median)} ms, p95 ${f0(h.p95)}, n=${h.n}
- StartedAt -> Startup complete : médiane ${f0(sc.median)} ms, p95 ${f0(sc.p95)}, n=${sc.n}
- statut JellyUX pendant : ${[...new Set(tg.disabled.map((r) => r.juxStatus))].join(',')}
- après : ${tg.after.plugins.map((p) => `${p.name} ${p.version} ${p.status}`).join('; ')}`);
}
for (const profile of ['std', 'lowend']) {
    const p = path.join(rawDir, `perf-bench-sections-interval-${profile}.csv`);
    if (!existsSync(p)) { continue; }
    const lines = readFileSync(p, 'utf8').trim().split(String.fromCharCode(10)).map((l) => l.trim()).slice(1).map((l) => l.split(','));
    console.log(`
### Sections selon le délai depuis l'appel précédent ${profile} (perf-bench-sections-interval-${profile}.csv)
`);
    console.log('| délai (s) | médiane ms | p95 ms | min | max | n |');
    console.log('|---|---|---|---|---|---|');
    for (const gap of [...new Set(lines.map((l) => l[1]))]) {
        const st = stats(lines.filter((l) => l[1] === gap).map((l) => Number(l[5])));
        console.log(`| ${gap} | ${f0(st.median)} | ${f0(st.p95)} | ${f0(st.min)} | ${f0(st.max)} | ${st.n} |`);
    }
}
for (const profile of ['std', 'lowend']) {
    const t = load(`perf-bench-tasks-${profile}.json`);
    if (!t) { continue; }
    console.log(`\n### Tâches planifiées ${profile} (perf-bench-tasks-${profile}.json)\n`);
    console.log('| Tâche | médiane ms | p95 ms | min | max | n | statuts |');
    console.log('|---|---|---|---|---|---|---|');
    for (const name of [...new Set(t.map((x) => x.task))]) {
        const xs = t.filter((x) => x.task === name); const s = stats(xs.map((x) => x.durationMs));
        console.log(`| ${name} | ${f0(s.median)} | ${f0(s.p95)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} | ${[...new Set(xs.map((x) => x.status))].join(',')} |`);
    }
}
