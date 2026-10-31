// Markdown summary of the C# (coverlet, Cobertura) and JS (Vitest, json-summary and lcov) line coverage,
// with the files the audit ranks as risky (TODO_V4.md 3.4, F-31), and the coverage of the plugin lines
// added since a base commit, with a warning under 80 % (TODO_V5 PROC-01.c). Informative only: it never
// fails the job, and prints the JS part alone when there is no Cobertura report (PROC-05.c). A new risk
// area enters the risky lists in the commit that introduces it (CONTRIBUTING.md "Testing", PROC-05.b).
//   node ci/coverage-summary.mjs <cobertura.xml> <coverage-summary.json> [--diff-base <commit>]
// Writes to $GITHUB_STEP_SUMMARY when set, to stdout otherwise; the warning always goes to stdout, where
// the runner reads workflow commands. lcov.info is read next to coverage-summary.json.

import { appendFileSync, existsSync, readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';

const RISKY_CS = ['Inject/', 'Controllers/', 'Personalized/', 'Widgets/Service/', 'Plugin.cs', 'PluginServiceRegistrator.cs'];
const RISKY_JS = ['jux-tab-injector.js', 'jux-homepage.js', 'jux-history.js', 'jux-progress.js', 'jux-watchlist.js', 'jux-card-hooks.js',
    'jux-featured.js', 'config.js', 'config-editors.js', 'config-sources.js', 'jux-loadsections-inject.js'];
// Left out of the Vitest coverage (vitest.config.js): a template the server fills in before splicing it
// into the home chunk, which v8 cannot parse as it is.
const NOT_INSTRUMENTED_JS = new Set(['jux-loadsections-inject.js']);
const PLUGIN_DIR = 'src/Jellyfin.Plugin.JuxHomepage/';
const CHANGED_LINES_WARNING = 80;

const pct = (covered, total) => (total === 0 ? 'n/a' : `${((100 * covered) / total).toFixed(1)} %`);

/**
 * Hits per line number per source file of a Cobertura report (a line counts once per file).
 * @param {string} xml
 * @returns {Map<string, Map<number, number>>}
 */
export function coberturaLineHits(xml) {
    const files = new Map();
    for (const cls of xml.matchAll(/<class [^>]*filename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g)) {
        const file = cls[1].replace(/\\/g, '/');
        const lines = files.get(file) ?? new Map();
        for (const line of cls[2].matchAll(/<line number="(\d+)" hits="(\d+)"/g)) {
            const number = Number(line[1]);
            lines.set(number, (lines.get(number) ?? 0) + Number(line[2]));
        }
        files.set(file, lines);
    }
    return files;
}

/**
 * Covered and total lines per source file of a Cobertura report (a line counts once per file).
 * @param {string} xml
 * @returns {Map<string, {covered: number, total: number}>}
 */
export function coberturaFiles(xml) {
    return new Map([...coberturaLineHits(xml)].map(([file, lines]) => [file, {
        covered: [...lines.values()].filter((hits) => hits > 0).length,
        total: lines.size
    }]));
}

/**
 * Hits per line number per source file of an lcov report (SF and DA records).
 * @param {string} lcov
 * @returns {Map<string, Map<number, number>>}
 */
export function lcovLineHits(lcov) {
    const files = new Map();
    let lines = null;
    for (const record of lcov.split(/\r?\n/)) {
        if (record.startsWith('SF:')) {
            lines = new Map();
            files.set(record.slice(3).replace(/\\/g, '/'), lines);
        } else if (record.startsWith('DA:') && lines) {
            const [number, hits] = record.slice(3).split(',').map(Number);
            lines.set(number, (lines.get(number) ?? 0) + hits);
        }
    }
    return files;
}

/**
 * Line numbers added per file by a `git diff -U0` output (new side; deleted files are skipped).
 * @param {string} diff
 * @returns {Map<string, Set<number>>}
 */
export function addedLines(diff) {
    const files = new Map();
    let current = null;
    for (const line of diff.split(/\r?\n/)) {
        if (line.startsWith('+++ ')) {
            const path = line.slice(4);
            current = path === '/dev/null' ? null : new Set();
            if (current) {
                files.set(path.replace(/^b\//, ''), current);
            }
            continue;
        }
        const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
        if (hunk && current) {
            const start = Number(hunk[1]);
            const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
            for (let n = start; n < start + count; n++) {
                current.add(n);
            }
        }
    }
    return files;
}

/**
 * Coverage of the added lines that a report instruments: a line no report knows (blank, comment,
 * declaration) counts in neither total. A report path matches a diff path when it ends with it at a path
 * segment boundary (reports hold absolute paths; a shorter report path never matches, so two files
 * sharing a name cannot swap hits). A changed file no report knows is listed in unreported (A-03.d).
 * @param {Map<string, Set<number>>} added
 * @param {Map<string, Map<number, number>>[]} reports
 * @returns {{covered: number, total: number, files: {file: string, covered: number, total: number}[], unreported: string[]}}
 */
export function changedLinesCoverage(added, reports) {
    const files = [];
    const unreported = [];
    for (const [file, lines] of added) {
        const hits = reports
            .flatMap((report) => [...report])
            .find(([path]) => `/${path}`.endsWith(`/${file}`))?.[1];
        if (!hits) {
            unreported.push(file);
            continue;
        }
        const instrumented = [...lines].filter((n) => hits.has(n));
        if (instrumented.length > 0) {
            files.push({ file, covered: instrumented.filter((n) => hits.get(n) > 0).length, total: instrumented.length });
        }
    }
    return {
        covered: files.reduce((sum, f) => sum + f.covered, 0),
        total: files.reduce((sum, f) => sum + f.total, 0),
        files,
        unreported
    };
}

/**
 * Sums the files whose path contains the given fragment at a path segment boundary.
 * @param {Map<string, {covered: number, total: number}>} files
 * @param {string} fragment
 * @returns {{covered: number, total: number}}
 */
export function sumMatching(files, fragment) {
    let covered = 0;
    let total = 0;
    for (const [file, c] of files) {
        if (`/${file}`.includes(`/${fragment}`)) {
            covered += c.covered;
            total += c.total;
        }
    }
    return { covered, total };
}

/**
 * The coverage table: C# total and risky C# areas (left out, with a note, without a Cobertura report),
 * then JS total and risky scripts.
 * @param {Map<string, {covered: number, total: number}> | null} cs Per-file C# coverage, or null.
 * @param {Record<string, {lines: {covered: number, total: number}}>} js Vitest json-summary.
 * @param {string} coberturaPath Named in the note when cs is null.
 * @returns {string[]}
 */
export function coverageRows(cs, js, coberturaPath) {
    const row = (scope, c) => `| ${scope} | ${c.covered} | ${c.total} | ${pct(c.covered, c.total)} |`;
    const csAll = cs && sumMatching(cs, '');
    return [
        ...(cs ? [] : [`_C#: no Cobertura report at \`${coberturaPath}\`, the C# rows are left out._`, '']),
        '| Scope | Covered | Lines | Coverage |',
        '|---|---|---|---|',
        ...(cs
            ? [`| **C# total** | ${csAll.covered} | ${csAll.total} | **${pct(csAll.covered, csAll.total)}** |`,
                ...RISKY_CS.map((f) => row(`C# ${f}`, sumMatching(cs, f)))]
            : []),
        `| **JS total** | ${js.total.lines.covered} | ${js.total.lines.total} | **${pct(js.total.lines.covered, js.total.lines.total)}** |`,
        ...RISKY_JS.map((f) => {
            if (NOT_INSTRUMENTED_JS.has(f)) {
                return `| JS ${f} | - | - | not instrumented (template) |`;
            }
            const entry = Object.entries(js).find(([file]) => file.replace(/\\/g, '/').endsWith(`/${f}`))?.[1];
            return entry ? row(`JS ${f}`, entry.lines) : `| JS ${f} | - | - | not measured |`;
        })
    ];
}

function changedLinesRows(base, csHits, jsHits) {
    if (!base || /^0+$/.test(base)) {
        return { rows: ['', '_Changed lines: no base commit for this run._'], warning: null };
    }
    let diff;
    try {
        diff = execFileSync('git', ['diff', '-U0', '--no-color', `${base}...HEAD`, '--', PLUGIN_DIR], {
            encoding: 'utf8',
            stdio: ['ignore', 'pipe', 'ignore'],
            // A long-lived base gives a diff of several megabytes (1 MB since the first commit).
            maxBuffer: 256 * 1024 * 1024
        });
    } catch {
        return { rows: ['', `_Changed lines: base ${base.slice(0, 7)} is not in the checked-out history._`], warning: null };
    }
    const added = new Map([...addedLines(diff)].filter(([file]) => /\.(cs|js)$/.test(file)));
    const result = changedLinesCoverage(added, [csHits, jsHits]);
    const rows = [
        '',
        `### Changed lines since ${base.slice(0, 7)} (warning under ${CHANGED_LINES_WARNING} %)`,
        '',
        '| File | Covered | Lines | Coverage |',
        '|---|---|---|---|',
        `| **All changed plugin lines** | ${result.covered} | ${result.total} | **${pct(result.covered, result.total)}** |`,
        ...result.files.map((f) => `| ${f.file} | ${f.covered} | ${f.total} | ${pct(f.covered, f.total)} |`),
        ...(result.unreported.length > 0
            ? ['', `_Changed files in no coverage report: ${result.unreported.map((f) => `\`${f}\``).join(', ')}._`]
            : [])
    ];
    // On the shown, rounded value, so 79.96 % reads 80.0 % and does not warn.
    const below = result.total > 0 && Number(((100 * result.covered) / result.total).toFixed(1)) < CHANGED_LINES_WARNING;
    return {
        rows,
        warning: below
            ? `::warning title=Changed lines coverage::${pct(result.covered, result.total)} of the ${result.total} changed plugin lines are covered (warning under ${CHANGED_LINES_WARNING} %).`
            : null
    };
}

function main() {
    const args = process.argv.slice(2);
    const baseIndex = args.indexOf('--diff-base');
    const base = baseIndex >= 0 ? args[baseIndex + 1] : null;
    const [coberturaPath, jsSummaryPath] = args.filter((_, i) => baseIndex < 0 || (i !== baseIndex && i !== baseIndex + 1));
    const xml = existsSync(coberturaPath) ? readFileSync(coberturaPath, 'utf8') : null;
    const csHits = xml ? coberturaLineHits(xml) : new Map();
    const js = JSON.parse(readFileSync(jsSummaryPath, 'utf8'));
    const lcovPath = join(dirname(jsSummaryPath), 'lcov.info');
    const jsHits = existsSync(lcovPath) ? lcovLineHits(readFileSync(lcovPath, 'utf8')) : new Map();

    const changed = changedLinesRows(base, csHits, jsHits);
    const rows = [
        '## Coverage (lines, informative)',
        '',
        ...coverageRows(xml ? coberturaFiles(xml) : null, js, coberturaPath),
        ...changed.rows
    ];

    const summary = process.env.GITHUB_STEP_SUMMARY;
    if (summary) {
        appendFileSync(summary, `${rows.join('\n')}\n`);
    } else {
        console.log(rows.join('\n'));
    }
    if (changed.warning) {
        console.log(changed.warning);
    }
}

if (process.argv[1]?.endsWith('coverage-summary.mjs')) {
    main();
}
