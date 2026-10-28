// Aggregates the perf-bench raw files of a series into a flat CSV and prints median / tail tables (markdown):
// the p95 from 20 values, the maximum and n below (V3). One folder per series (the JUX_BENCH_OUT of the run,
// fingerprint.json at its top, bench-fingerprint.mjs).
//   node bench/perf-bench-summarize.mjs [<series folder>] [--witness <native series folder>]
//   node bench/perf-bench-summarize.mjs --compare <series A> <series B> [--allow <field>,<field>]
// (--compare checks the two fingerprints only; each side keeps its own witness requirement when summarized.)
// A series with Homepage is summarized only next to a native witness on the same data set (PROC-02.g): the
// folder given by --witness, otherwise the sibling folder native-<suffix> (bench/perf-matrix.mjs layout,
// e.g. hp-srvperso next to native-srvperso). --compare refuses two series whose fingerprints differ outside
// the declared variable fields (PROC-02.b). --allow adds the fields a comparison changes on purpose (e.g.
// server.version,server.image,counts.ItemCount for native 10.11 against native 12.3), printed with the result.
//   node bench/perf-bench-summarize.mjs
import { readFileSync, existsSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { rawDir, stats, tail, TAIL_HEADER } from './perf-bench-common.mjs';
import { compareFingerprints, VARIABLE_FIELDS } from './bench-fingerprint.mjs';
import { matchManifest } from './section-manifest.mjs';

const f1 = (x) => (x === null || x === undefined ? 'n/a' : (Math.round(x * 10) / 10).toLocaleString('fr-FR'));
const f0 = (x) => (x === null || x === undefined ? 'n/a' : Math.round(x).toLocaleString('fr-FR'));
const kib = (x) => (x === null || x === undefined ? 'n/a' : (Math.round(x / 1024 * 10) / 10).toLocaleString('fr-FR'));
const loadFrom = (dir, name) => { const p = path.join(dir, name); return existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')) : null; };

// Files a plugin injects into the web client (BENCH-01): the Homepage assets served under /JuxHomepage/ (its API
// routes start with a capital letter and are left out; the loadSections fragment is spliced into a Jellyfin
// chunk and cannot be told apart), and the KefinTweaks files (jsDelivr, JavaScript Injector).
const INJECTED = { homepage: /\/JuxHomepage\/[a-z][^/?]*\.(js|css)(\?|$)/, kefintweaks: /KefinTweaks|kefinTweaks|\/JavaScriptInjector\// };

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
    const homepageAssets = list.filter((r) => INJECTED.homepage.test(r.url));
    const ktAssets = list.filter((r) => INJECTED.kefintweaks.test(r.url));
    v['injected.homepageFiles'] = homepageAssets.length; v['injected.homepageBytes'] = homepageAssets.reduce((a, r) => a + r.bytes, 0);
    v['injected.kefintweaksFiles'] = ktAssets.length; v['injected.kefintweaksBytes'] = ktAssets.reduce((a, r) => a + r.bytes, 0);
    for (const [k, x] of Object.entries(h.chain || {})) { v[`chain.${k}`] = x; }
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

/** Target of a series for the section manifest, from its fingerprint. */
export function targetOf(fingerprint) {
    if (!fingerprint) { return 'native'; }
    if (fingerprint.homepage) { return 'homepage'; }
    return Object.keys(fingerprint.plugins || {}).some((p) => /kefin|javascript injector/i.test(p)) ? 'kefintweaks' : 'native';
}

/** Labels of the home rows that showed cards in any run of a browser set. */
function rowLabels(set) {
    return [...new Set(set.runs.flatMap(({ run }) => (run.home.sections || []).filter((s) => s.card !== null).map((s) => s.cls)))];
}

/** Prints the tables of one series folder and writes its flat CSV there. */
export function summarizeSeries(dir) {
    const load = (name) => loadFrom(dir, name);
    const rows = [];
    const browserSets = [];
    for (const [profile, viewport] of [['std', 'desktop'], ['std', 'mobile'], ['lowend', 'desktop'], ['lowend', 'mobile'], ['boot', 'desktop']]) {
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
    writeFileSync(path.join(dir, 'perf-bench-browser-flat.csv'), 'profile,viewport,mode,cycle,run,indicator,value\n' + rows.map((r) => r.join(',')).join('\n') + '\n');

    const fmtFor = (k) => (/bytes|Bytes/.test(k) ? kib : /Ms$|MiB$/.test(k) ? f0 : f1);
    for (const set of browserSets) {
        console.log(`\n### Navigateur ${set.profile} ${set.viewport} (${set.file})\n`);
        console.log(`| Indicateur | froid médiane | froid ${TAIL_HEADER} | chaud médiane | chaud ${TAIL_HEADER} | n froid / chaud |`);
        console.log('|---|---|---|---|---|---|');
        const keys = [...new Set([...set.cold, ...set.warm].flatMap((v) => Object.keys(v)))];
        for (const k of keys) {
            const c = stats(set.cold.map((v) => v[k])); const w = stats(set.warm.map((v) => v[k]));
            const f = fmtFor(k);
            console.log(`| ${k}${/bytes|Bytes/.test(k) ? ' (KiB)' : ''} | ${f(c.median)} | ${tail(c, f)} | ${f(w.median)} | ${tail(w, f)} | ${c.n} / ${w.n} |`);
        }
        for (const [k, arr] of Object.entries(set.mem)) { const s = stats(arr); console.log(`| mem.${k} (MiB) | ${f1(s.median)} | ${tail(s, f1)} | | | ${s.n} |`); }
        console.log('\nchain.* : ms depuis le début de la navigation (Resource Timing) ; chain.loadSectionsCallEstMs est une estimation (début de la 1re requête Sections ou UserViews après le chunk, sinon fin du chunk : chain.loadSectionsCallFromChunk = 1), aucune marque dans le produit.');
        const inj = (k) => stats(set.cold.map((v) => v[k])).median;
        console.log(`Octets injectés (froid, médiane, BENCH-01) : Homepage ${kib(inj('injected.homepageBytes'))} KiB en ${f0(inj('injected.homepageFiles'))} fichiers / KefinTweaks ${kib(inj('injected.kefintweaksBytes'))} KiB en ${f0(inj('injected.kefintweaksFiles'))} fichiers`);
        const rows = matchManifest(targetOf(load('fingerprint.json')), rowLabels(set));
        console.log(`Manifeste de sections (${targetOf(load('fingerprint.json'))}) : présentes ${rows.present.join(', ') || 'aucune'} ; absentes ${rows.missing.join(', ') || 'aucune'} ; hors manifeste ${rows.outside.join(', ') || 'aucune'}`);

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
        const p = path.join(dir, `perf-bench-endpoints-${profile}.csv`);
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
        console.log(`| Endpoint | froid médiane | froid ${TAIL_HEADER} | chaud médiane | chaud ${TAIL_HEADER} | n froid / chaud | octets | HTTP |`);
        console.log('|---|---|---|---|---|---|---|---|');
        for (const ep of order) {
            const e = m.get(ep); const c = stats(e.cold); const w = stats(e.warm); const ct = stats(e.coldT); const wt = stats(e.warmT);
            console.log(`| ${ep} | ${f1(c.median)} (${f1(ct.median)}) | ${tail(c, f1)} | ${f1(w.median)} (${f1(wt.median)}) | ${tail(w, f1)} | ${c.n} / ${w.n} | ${e.size} | ${[...e.codes].join('/')} |`);
        }
    }

    // Startup and memory at rest.
    for (const profile of ['std', 'lowend']) {
        const recs = [];
        // Only restarts timed by restartAndMeasure() carry the log milestones (a matrix cell records healthyMs and readyMs).
        const add = (r, source) => { if (r && r.markersFromProcessStartMs) { recs.push({ ...r, source }); } };
        const b = load(`perf-bench-browser-${profile}-desktop.json`); if (b) { b.cycles.forEach((c) => add({ ...c.startup, memRest: c.memRest }, 'browser-desktop')); }
        const bm = load(`perf-bench-browser-${profile}-mobile.json`); if (bm) { bm.cycles.forEach((c) => add({ ...c.startup, memRest: c.memRest }, 'browser-mobile')); }
        const e = load(`perf-bench-startup-endpoints-${profile}.json`); if (e) { e.forEach((r) => add(r, 'endpoints')); }
        if (recs.length === 0) { continue; }
        console.log(`\n### Démarrage ${profile} (n = ${recs.length} redémarrages ; ms depuis State.StartedAt du conteneur)\n`);
        console.log(`| Jalon | médiane | ${TAIL_HEADER} | min | max | n |`);
        console.log('|---|---|---|---|---|---|');
        const keys = Object.keys(recs[0].markersFromProcessStartMs || {});
        for (const k of keys) { const s = stats(recs.map((r) => r.markersFromProcessStartMs[k])); console.log(`| ${k} | ${f0(s.median)} | ${tail(s, f0)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} |`); }
        for (const k of ['startedToHealthyMs', 'cmdToHealthyMs']) { const s = stats(recs.map((r) => r[k])); console.log(`| ${k} | ${f0(s.median)} | ${tail(s, f0)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} |`); }
        const s = stats(recs.map((r) => r.memRest && r.memRest.memMiB)); console.log(`| mémoire au repos (MiB, 30 s après /health) | ${f1(s.median)} | ${tail(s, f1)} | ${f1(s.min)} | ${f1(s.max)} | ${s.n} |`);
        const d = stats(recs.map((r) => r.markersFromProcessStartMs.registeredHomeHtml - r.markersFromProcessStartMs.juxIndexRegistered));
        console.log(`| durée enregistrement patchs JellyUX (index.html -> home-html) | ${f0(d.median)} | ${tail(d, f0)} | ${f0(d.min)} | ${f0(d.max)} | ${d.n} |`);
    }

    for (const profile of ['std', 'lowend']) {
        const tg = load(`perf-bench-plugin-toggle-${profile}.json`);
        if (!tg) { continue; }
        const s = stats(tg.disabled.map((r) => r.memRest.memMiB));
        const h = stats(tg.disabled.map((r) => r.startedToHealthyMs));
        const sc = stats(tg.disabled.map((r) => r.markersFromProcessStartMs.startupComplete));
        console.log(`
    ### JellyUX désactivé ${profile} (perf-bench-plugin-toggle-${profile}.json)
    - mémoire au repos : médiane ${f1(s.median)} MiB, ${TAIL_HEADER} ${tail(s, f1)}, n=${s.n}
    - StartedAt -> /health : médiane ${f0(h.median)} ms, ${TAIL_HEADER} ${tail(h, f0)}, n=${h.n}
    - StartedAt -> Startup complete : médiane ${f0(sc.median)} ms, ${TAIL_HEADER} ${tail(sc, f0)}, n=${sc.n}
    - statut JellyUX pendant : ${[...new Set(tg.disabled.map((r) => r.juxStatus))].join(',')}
    - après : ${tg.after.plugins.map((p) => `${p.name} ${p.version} ${p.status}`).join('; ')}`);
    }
    for (const profile of ['std', 'lowend']) {
        const p = path.join(dir, `perf-bench-sections-interval-${profile}.csv`);
        if (!existsSync(p)) { continue; }
        const lines = readFileSync(p, 'utf8').trim().split(String.fromCharCode(10)).map((l) => l.trim()).slice(1).map((l) => l.split(','));
        console.log(`
    ### Sections selon le délai depuis l'appel précédent ${profile} (perf-bench-sections-interval-${profile}.csv)
    `);
        console.log(`| délai (s) | médiane ms | ${TAIL_HEADER} ms | min | max | n |`);
        console.log('|---|---|---|---|---|---|');
        for (const gap of [...new Set(lines.map((l) => l[1]))]) {
            const st = stats(lines.filter((l) => l[1] === gap).map((l) => Number(l[5])));
            console.log(`| ${gap} | ${f0(st.median)} | ${tail(st, f0)} | ${f0(st.min)} | ${f0(st.max)} | ${st.n} |`);
        }
    }
    for (const profile of ['std', 'lowend']) {
        const t = load(`perf-bench-tasks-${profile}.json`);
        if (!t) { continue; }
        console.log(`\n### Tâches planifiées ${profile} (perf-bench-tasks-${profile}.json)\n`);
        console.log(`| Tâche | médiane ms | ${TAIL_HEADER} ms | min | max | n | statuts |`);
        console.log('|---|---|---|---|---|---|---|');
        for (const name of [...new Set(t.map((x) => x.task))]) {
            const xs = t.filter((x) => x.task === name); const s = stats(xs.map((x) => x.durationMs));
            console.log(`| ${name} | ${f0(s.median)} | ${tail(s, f0)} | ${f0(s.min)} | ${f0(s.max)} | ${s.n} | ${[...new Set(xs.map((x) => x.status))].join(',')} |`);
        }
    }
}

// A witness differs from its series by Homepage only: a series running other plugins (KefinTweaks, the production
// stack) is not a native witness. The warm-up line is written by Homepage, so a native witness never has one.
const WITNESS_VARIABLE = [...VARIABLE_FIELDS, 'plugins.JellyUX Homepage', 'plugins.File Transformation', 'homepage.', 'warmup.built'];

/** Why a series cannot be summarized against its witness (empty: it can). */
export function checkWitness(series, witness) {
    if (!series) { return ['no fingerprint.json in the series folder']; }
    if (series.homepage === null) { return []; }
    if (!witness) { return ['no native witness for a series with Homepage (--witness <folder>, or a sibling native-<suffix> folder)']; }
    if (witness.homepage !== null) { return ['the witness runs Homepage: it is not a native witness']; }
    return compareFingerprints(series, witness, WITNESS_VARIABLE).map((d) => `witness differs: ${d}`);
}

function witnessFolder(dir, explicit) {
    if (explicit) { return path.resolve(explicit); }
    const name = path.basename(dir);
    const dash = name.indexOf('-');
    return dash > 0 ? path.join(path.dirname(dir), `native${name.slice(dash)}`) : null;
}

function main() {
    const argv = process.argv.slice(2);
    const option = (n) => { const i = argv.indexOf(`--${n}`); return i >= 0 ? argv[i + 1] : null; };
    const ci = argv.indexOf('--compare');
    if (ci >= 0) {
        const [a, b] = [path.resolve(argv[ci + 1]), path.resolve(argv[ci + 2])];
        const fa = loadFrom(a, 'fingerprint.json');
        const fb = loadFrom(b, 'fingerprint.json');
        if (!fa || !fb) { console.error(`--compare: fingerprint.json missing in ${!fa ? a : b}`); process.exit(1); }
        const allowed = (option('allow') || '').split(',').filter(Boolean);
        const diff = compareFingerprints(fa, fb, [...VARIABLE_FIELDS, ...allowed]);
        if (diff.length > 0) {
            console.error(`--compare refused: the two series ran on different sets or configurations\n- ${diff.join('\n- ')}`);
            process.exit(1);
        }
        const at = (f, key) => JSON.stringify(key.split('.').reduce((o, p) => (o ? o[p] : undefined), f));
        if (allowed.length > 0) { console.log(`Écarts déclarés (--allow) : ${allowed.map((k) => `${k} ${at(fa, k)} -> ${at(fb, k)}`).join(' ; ')}\n`); }
        console.log(`## A : ${a}`); summarizeSeries(a);
        console.log(`\n## B : ${b}`); summarizeSeries(b);
        return;
    }
    const dir = path.resolve(argv.find((x, i) => !x.startsWith('--') && (i === 0 || argv[i - 1] !== '--witness')) || rawDir);
    const wdir = witnessFolder(dir, option('witness'));
    const problems = checkWitness(loadFrom(dir, 'fingerprint.json'), wdir ? loadFrom(wdir, 'fingerprint.json') : null);
    if (problems.length > 0) { console.error(`refused: ${dir}\n- ${problems.join('\n- ')}`); process.exit(1); }
    summarizeSeries(dir);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) { main(); }
