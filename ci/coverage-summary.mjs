// Markdown summary of the C# (coverlet, Cobertura) and JS (Vitest, json-summary) line coverage, with
// the files the audit ranks as risky (TODO_V4.md 3.4, F-31). Informative only: no threshold.
//   node ci/coverage-summary.mjs <cobertura.xml> <coverage-summary.json> >> "$GITHUB_STEP_SUMMARY"

import { readFileSync } from 'node:fs';

const RISKY_CS = ['Inject/', 'Controllers/', 'Personalized/', 'Plugin.cs', 'PluginServiceRegistrator.cs'];
const RISKY_JS = ['jux-tab-injector.js', 'jux-homepage.js', 'jux-history.js', 'jux-progress.js', 'jux-watchlist.js'];

const pct = (covered, total) => (total === 0 ? 'n/a' : `${((100 * covered) / total).toFixed(1)} %`);

/**
 * Covered and total lines per source file of a Cobertura report (a line counts once per file).
 * @param {string} xml
 * @returns {Map<string, {covered: number, total: number}>}
 */
export function coberturaFiles(xml) {
    const files = new Map();
    for (const cls of xml.matchAll(/<class [^>]*filename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g)) {
        const file = cls[1].replace(/\\/g, '/');
        const lines = files.get(file)?.lines ?? new Map();
        for (const line of cls[2].matchAll(/<line number="(\d+)" hits="(\d+)"/g)) {
            lines.set(line[1], (lines.get(line[1]) ?? 0) + Number(line[2]));
        }
        files.set(file, { lines });
    }
    return new Map([...files].map(([file, { lines }]) => [file, {
        covered: [...lines.values()].filter((hits) => hits > 0).length,
        total: lines.size
    }]));
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

function main() {
    const [coberturaPath, jsSummaryPath] = process.argv.slice(2);
    const cs = coberturaFiles(readFileSync(coberturaPath, 'utf8'));
    const js = JSON.parse(readFileSync(jsSummaryPath, 'utf8'));

    const csAll = sumMatching(cs, '');
    const rows = [
        '## Coverage (lines, informative)',
        '',
        '| Scope | Covered | Lines | Coverage |',
        '|---|---|---|---|',
        `| **C# total** | ${csAll.covered} | ${csAll.total} | **${pct(csAll.covered, csAll.total)}** |`,
        ...RISKY_CS.map((f) => {
            const c = sumMatching(cs, f);
            return `| C# ${f} | ${c.covered} | ${c.total} | ${pct(c.covered, c.total)} |`;
        }),
        `| **JS total** | ${js.total.lines.covered} | ${js.total.lines.total} | **${pct(js.total.lines.covered, js.total.lines.total)}** |`,
        ...RISKY_JS.map((f) => {
            const entry = Object.entries(js).find(([file]) => file.replace(/\\/g, '/').endsWith(`/${f}`))?.[1];
            return entry
                ? `| JS ${f} | ${entry.lines.covered} | ${entry.lines.total} | ${pct(entry.lines.covered, entry.lines.total)} |`
                : `| JS ${f} | - | - | not measured |`;
        })
    ];
    console.log(rows.join('\n'));
}

if (process.argv[1]?.endsWith('coverage-summary.mjs')) {
    main();
}
