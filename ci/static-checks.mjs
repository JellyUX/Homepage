// Static checks for the project rules (TODO_V4.md rule G2): em dashes, color literals, banned fonts,
// targetAbi coherence, commit messages and code duplication. Run from the repository root:
//   node ci/static-checks.mjs [--commits <git range>]
// Errors fail the run; warnings are reported only. Both are printed as GitHub annotations.

import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const EM_DASH = String.fromCharCode(0x2014);
const BANNED_FONTS = ['Inter', 'Poppins', 'Manrope', 'Outfit', 'Plus Jakarta Sans', 'Space Grotesk'];
const COMMIT_TYPES = ['feat', 'fix', 'refactor', 'style', 'docs', 'chore', 'test', 'revert'];
const WEB_DIR = 'src/Jellyfin.Plugin.JuxHomepage/Web/';
const MIN_ABI = '10.11.9.0';
const FIRST_VERSION_REQUIRING_MIN_ABI = '2.4.0.0';
const BINARY_EXTENSIONS = new Set(['.png', '.jpg', '.jpeg', '.gif', '.ico', '.webp', '.dll', '.pdb', '.zip', '.mkv', '.woff', '.woff2']);
export const BASELINE_PATH = 'ci/duplication-baseline.json';
// Ratcheted budgets (TODO_V5 rule R3): a value may only rise in a commit carrying a "Budget:" trailer.
export const BUDGET_FILES = ['ci/perf-budget.json', 'ci/asset-budget.json', 'ci/duplication-baseline.json', 'ci/dead-code-allowlist.json'];

const HEX_COLOR = /(?<![\w&#-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])/g;
const COLOR_FUNCTION = /\b(?:rgba?|hsla?)\(\s*[\d.]/g;
const VAR_FALLBACK = /var\(\s*--[\w-]+\s*,\s*(?:#[0-9a-fA-F]{3,8}|(?:rgba?|hsla?)\([^()]*\))\s*\)/g;
// A comment that defers work must name the registry id that tracks it (TODO_V5 rule R8, PROC-04.b).
const DEFERRED_MARKER = /\bTODO\b|\bFIXME\b|future phase|worth revisiting/gi;
// Only the registry prefixes: V5 findings, hypotheses, anomalies and decisions, V4 findings (A-03.e).
const REGISTRY_ID = /\b(?:PERF|FIAB|QUAL|PROC|KTW|ADM|H|A|D|F)-\d{2}\b/;
const TEST_FILE = /(?:^test\/.*\.test\.js|^e2e\/.*\.spec\.mjs|\.Tests\/.*Tests\.cs)$/;
const MARKER_EXTENSIONS = new Set(['.cs', '.js', '.mjs', '.css', '.html']);

/**
 * Blanks out comments while keeping line breaks, so reported line numbers stay right.
 * @param {string} text
 * @param {string} ext File extension, including the dot.
 * @returns {string}
 */
export function stripComments(text, ext) {
    const blank = (m) => m.replace(/[^\n]/g, ' ');
    if (ext === '.js' || ext === '.mjs' || ext === '.cs') {
        return stripCodeComments(text, ext === '.cs');
    }
    let out = text.replace(/\/\*[\s\S]*?\*\//g, blank);
    if (ext === '.html') {
        out = out.replace(/<!--[\s\S]*?-->/g, blank);
    }
    if (ext === '.html') {
        out = out.replace(/(^|[^:\\'"`])\/\/[^\n]*/g, (m, lead) => lead + blank(m.slice(lead.length)));
    }
    return out;
}

// A slash after one of these (or at the start) opens a JS regex literal, not a division.
const REGEX_LEAD = /[(,=:[!&|?{};+\-*%<>~^]|^$|\b(?:return|typeof|case|in|of|delete|void|throw)$/;

/**
 * Blanks the comments of JS or C# source, skipping strings, template literals, JS regex literals and
 * C# verbatim strings (A-03.e: a "//" in a string is not a comment).
 * @param {string} text
 * @param {boolean} csharp
 * @returns {string}
 */
function stripCodeComments(text, csharp) {
    const out = text.split('');
    const blank = (from, to) => {
        for (let k = from; k < to; k++) {
            if (out[k] !== '\n') out[k] = ' ';
        }
    };
    let i = 0;
    while (i < text.length) {
        const c = text[i];
        const next = text[i + 1];
        if (c === '/' && next === '/') {
            const end = text.indexOf('\n', i);
            const stop = end < 0 ? text.length : end;
            blank(i, stop);
            i = stop;
        } else if (c === '/' && next === '*') {
            const end = text.indexOf('*/', i + 2);
            const stop = end < 0 ? text.length : end + 2;
            blank(i, stop);
            i = stop;
        } else if (csharp && c === '@' && next === '"') {
            i += 2;
            while (i < text.length && !(text[i] === '"' && text[i + 1] !== '"')) {
                i += text[i] === '"' ? 2 : 1;
            }
            i++;
        } else if (c === '"' || c === "'" || c === '`') {
            i++;
            while (i < text.length && text[i] !== c && (c === '`' || text[i] !== '\n')) {
                i += text[i] === '\\' ? 2 : 1;
            }
            i++;
        } else if (!csharp && c === '/' && REGEX_LEAD.test(text.slice(Math.max(0, i - 40), i).trimEnd().match(/(\w+|\S)$/)?.[1] ?? '')) {
            i++;
            let inClass = false;
            while (i < text.length && text[i] !== '\n' && (inClass || text[i] !== '/')) {
                if (text[i] === '[') inClass = true;
                else if (text[i] === ']') inClass = false;
                i += text[i] === '\\' ? 2 : 1;
            }
            i++;
        } else {
            i++;
        }
    }
    return out.join('');
}

/**
 * Finds the lines holding an em dash.
 * @param {string} text
 * @returns {number[]} 1-based line numbers.
 */
export function findEmDashes(text) {
    return text.split('\n').flatMap((line, i) => (line.includes(EM_DASH) ? [i + 1] : []));
}

/**
 * Finds hardcoded colors outside a var() fallback, comments excluded.
 * @param {string} text
 * @param {string} ext File extension, including the dot.
 * @returns {{line: number, match: string}[]}
 */
export function findColorLiterals(text, ext) {
    const lines = stripComments(text, ext).split('\n');
    const found = [];
    lines.forEach((raw, i) => {
        const line = raw.replace(VAR_FALLBACK, '');
        for (const re of [HEX_COLOR, COLOR_FUNCTION]) {
            for (const m of line.matchAll(re)) {
                found.push({ line: i + 1, match: m[0] });
            }
        }
    });
    return found;
}

/**
 * Finds the comments that defer work (marker words) without a registry id on the same line.
 * @param {string} text
 * @param {string} ext File extension, including the dot.
 * @returns {{line: number, match: string}[]}
 */
export function findUntrackedMarkers(text, ext) {
    const stripped = stripComments(text, ext);
    // Keep only what stripComments blanked: the comments, at their original positions.
    // split(''), not Array.from: UTF-16 units, the indexing stripComments keeps.
    const comments = text.split('').map((c, i) => (c === '\n' ? '\n' : c === stripped[i] ? ' ' : c)).join('');
    return comments.split('\n').flatMap((line, i) => (REGISTRY_ID.test(line)
        ? []
        : [...line.matchAll(DEFERRED_MARKER)].map((m) => ({ line: i + 1, match: m[0] }))));
}

/**
 * Finds banned font family names, as whole words.
 * @param {string} text
 * @returns {{line: number, match: string}[]}
 */
export function findBannedFonts(text) {
    const re = new RegExp(`\\b(?:${BANNED_FONTS.map((f) => f.replace(/ /g, '\\s+')).join('|')})\\b`, 'g');
    return text.split('\n').flatMap((line, i) => [...line.matchAll(re)].map((m) => ({ line: i + 1, match: m[0] })));
}

function compareVersions(a, b) {
    const pa = a.split('.').map(Number);
    const pb = b.split('.').map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) {
            return d;
        }
    }
    return 0;
}

/**
 * Checks that build.yaml, manifest.json and the SDK the plugin compiles against agree.
 * @param {{buildYaml: string, manifest: string, csproj: string}} files
 * @returns {string[]} Error messages.
 */
export function checkTargetAbi({ buildYaml, manifest, csproj }) {
    const errors = [];
    const abi = /^targetAbi:\s*"([0-9.]+)"/m.exec(buildYaml)?.[1];
    if (!abi) {
        return ['build.yaml has no targetAbi line.'];
    }
    if (compareVersions(abi, MIN_ABI) < 0) {
        errors.push(`build.yaml targetAbi ${abi} is below the required minimum ${MIN_ABI}.`);
    }

    const offending = JSON.parse(manifest)[0].versions
        .filter((v) => compareVersions(v.version, FIRST_VERSION_REQUIRING_MIN_ABI) >= 0)
        .filter((v) => compareVersions(v.targetAbi, MIN_ABI) < 0)
        .map((v) => v.version);
    if (offending.length > 0) {
        errors.push(`manifest.json versions below targetAbi ${MIN_ABI}: ${offending.join(', ')}.`);
    }

    // A build against a newer SDK than targetAbi does not load on the servers targetAbi admits.
    const sdk = /<JellyfinVersion Condition="[^"]*">([0-9.]+)</.exec(csproj)?.[1];
    if (!sdk) {
        errors.push('The plugin .csproj has no default JellyfinVersion.');
    } else if (compareVersions(sdk, abi) !== 0) {
        errors.push(`The plugin compiles against Jellyfin ${sdk}, but build.yaml declares targetAbi ${abi}.`);
    }
    return errors;
}

/**
 * Checks one commit against the commit rules.
 * @param {{sha: string, subject: string, body: string, files: string[], added: Record<string, number>}} commit
 *   added: the lines each file gains (git diff-tree --numstat).
 * @returns {{errors: string[], warnings: string[]}}
 */
export function checkCommit({ sha, subject, body, files, added }) {
    const errors = [];
    const warnings = [];
    const short = sha.slice(0, 7);
    const typeRe = new RegExp(`^(${COMMIT_TYPES.join('|')})(\\([\\w./-]+\\))?!?: \\S`);
    // git revert's default subject, which the Conventional Commits parser reads as a revert.
    const type = typeRe.exec(subject)?.[1] ?? (/^Revert ".+"$/.test(subject) ? 'revert' : undefined);

    if (!type) {
        errors.push(`${short}: subject "${subject}" is not a Conventional Commit (${COMMIT_TYPES.join(', ')}).`);
    }
    if (/^co-authored-by:/im.test(body)) {
        errors.push(`${short}: Co-Authored-By trailers are not allowed.`);
    }
    if (subject.includes(EM_DASH) || body.includes(EM_DASH)) {
        errors.push(`${short}: the commit message contains an em dash.`);
    }

    if (type === 'fix') {
        // Every fix, server or client, ships the test that proves it (TODO_V5 PROC-mineur.a): lines added
        // to a test file, not a fixture, a helper or a deletion (A-03.c).
        const touchesTests = files.some((f) => TEST_FILE.test(f) && (added[f] ?? 0) > 0);
        if (!touchesTests && !/^untestable:\s*\S/im.test(body)) {
            errors.push(`${short}: a fix: without a test needs an "Untestable: <reason>" trailer.`);
        }
        if (files.length > 3 && body.trim() === '') {
            warnings.push(`${short}: a fix: touching ${files.length} files has no body explaining it.`);
        }
    }
    return { errors, warnings };
}

/**
 * Measures of a ratcheted file, keyed "table/name", with the tables they come from:
 * - a table of numbers (endpoints, assets, totals): each number;
 * - a table of strings (an allowlist): its entry count, as "table/#entries";
 * - an array of {files, copies|count} (families, pairs): the copies per set of files.
 * @param {string | null} text
 * @returns {{values: Record<string, number>, tables: Set<string>, listed: Set<string>}}
 */
function ratchetMeasures(text) {
    const values = {};
    const tables = new Set();
    const listed = new Set();
    for (const [table, content] of Object.entries(text === null ? {} : JSON.parse(text))) {
        if (Array.isArray(content)) {
            tables.add(table);
            for (const item of content) {
                const key = `${table}/${(item.files ?? []).join(' | ')}`;
                values[key] = (values[key] ?? 0) + (item.count ?? item.copies ?? 1);
                listed.add(key);
            }
        } else if (content !== null && typeof content === 'object') {
            tables.add(table);
            const strings = Object.values(content).filter((v) => typeof v === 'string').length;
            if (strings > 0) {
                values[`${table}/#entries`] = strings;
                listed.add(`${table}/#entries`);
            }
            for (const [key, value] of Object.entries(content).filter(([, v]) => typeof v === 'number')) {
                values[`${table}/${key}`] = value;
            }
        }
    }
    return { values, tables, listed };
}

/**
 * Checks that a commit raising a ratcheted budget says why in a "Budget: <reason>" trailer: a number
 * that goes up, an allowlist that gets longer, or a set of files with more clones (a new set counts
 * as a rise from 0). A new number in a table of numbers (a new endpoint or asset), a table the file
 * did not have before, and the commit that creates or deletes the file need no trailer.
 * @param {{sha: string, file: string, before: string | null, after: string | null, body: string}} change
 *   The budget file's text before and after the commit (null when absent).
 * @returns {string[]} Errors.
 */
export function checkBudget({ sha, file, before, after, body, label = sha.slice(0, 7) }) {
    if (before === null || after === null || /^budget:\s*\S/im.test(body)) {
        return [];
    }
    const previous = ratchetMeasures(before);
    const current = ratchetMeasures(after);
    const raised = Object.entries(current.values)
        .filter(([key]) => previous.tables.has(key.split('/')[0]))
        .filter(([key, value]) => {
            const was = previous.values[key] ?? (current.listed.has(key) ? 0 : undefined);
            return was !== undefined && value > was;
        })
        .map(([key, value]) => `"${key}" ${previous.values[key] ?? 0} -> ${value}`);
    return raised.length === 0
        ? []
        : [`${label}: ${file} raises ${raised.join(', ')} without a "Budget: <reason>" trailer.`];
}

/**
 * Checks the budget files of a push, commit by commit and over the whole push: a budget deleted then put
 * back higher in two commits passes each commit but not the push (A-03.a). The push needs a "Budget:"
 * trailer in one of the commits that touch the file.
 * @param {{base: string, head: string, commits: {sha: string, body: string, files: string[]}[],
 *   show: (spec: string) => string | null}} push show reads "<rev>:<file>", null when absent.
 * @returns {string[]} Errors.
 */
export function checkPushBudgets({ base, head, commits, show }) {
    const errors = [];
    for (const file of BUDGET_FILES) {
        const touching = commits.filter((c) => c.files.includes(file));
        if (touching.length === 0) {
            continue;
        }
        const perCommit = touching.flatMap((c) =>
            checkBudget({ sha: c.sha, file, before: show(`${c.sha}^:${file}`), after: show(`${c.sha}:${file}`), body: c.body }));
        errors.push(...perCommit);
        if (perCommit.length === 0) {
            errors.push(...checkBudget({
                sha: head,
                file,
                before: show(`${base}:${file}`),
                after: show(`${head}:${file}`),
                body: touching.map((c) => c.body).join('\n'),
                label: `${base.slice(0, 7)}..${head.slice(0, 7)}`
            }));
        }
    }
    return errors;
}

/**
 * Groups jscpd clone pairs into families of overlapping locations.
 * @param {{firstFile: {name: string, start: number, end: number}, secondFile: {name: string, start: number, end: number}}[]} duplicates
 * @returns {{files: string[], copies: number}[]} Families with at least 3 copies.
 */
export function groupCloneFamilies(duplicates) {
    const locations = [];
    const parent = [];
    const find = (x) => (parent[x] === x ? x : (parent[x] = find(parent[x])));
    const locate = ({ name, start, end }) => {
        const file = name.replace(/\\/g, '/');
        let i = locations.findIndex((l) => l.file === file && l.start <= end && start <= l.end);
        if (i < 0) {
            locations.push({ file, start, end });
            i = locations.length - 1;
            parent[i] = i;
        } else {
            locations[i].start = Math.min(locations[i].start, start);
            locations[i].end = Math.max(locations[i].end, end);
        }
        return i;
    };
    for (const d of duplicates) {
        parent[find(locate(d.firstFile))] = find(locate(d.secondFile));
    }

    const groups = new Map();
    locations.forEach((l, i) => {
        const root = find(i);
        groups.set(root, [...(groups.get(root) ?? []), l]);
    });
    return [...groups.values()]
        .filter((g) => g.length >= 3)
        .map((g) => ({ files: [...new Set(g.map((l) => l.file))].sort(), copies: g.length }));
}

/**
 * Compares clone families with the baseline: per set of files, no more families and no more copies.
 * @param {{files: string[], copies: number}[]} families
 * @param {{files: string[], copies: number}[]} baseline
 * @returns {{errors: string[], warnings: string[]}}
 */
export function compareWithBaseline(families, baseline) {
    const byKey = (list) => {
        const map = new Map();
        for (const f of list) {
            const key = f.files.join(' | ');
            map.set(key, [...(map.get(key) ?? []), f.copies].sort((a, b) => b - a));
        }
        return map;
    };
    const current = byKey(families);
    const allowed = byKey(baseline);
    const errors = [];
    const warnings = [];

    for (const [key, copies] of current) {
        const limit = allowed.get(key) ?? [];
        if (copies.length > limit.length || copies.some((c, i) => c > limit[i])) {
            errors.push(`code repeated 3+ times beyond the baseline (${copies.join(', ')} copies, allowed ${limit.join(', ') || 'none'}): ${key}`);
        }
    }
    for (const [key, copies] of allowed) {
        const now = current.get(key) ?? [];
        if (now.length < copies.length || now.some((c, i) => c < copies[i])) {
            errors.push(`duplication went down, shrink ${BASELINE_PATH} in the same commit: ${key}`);
        }
    }
    return { errors, warnings };
}

/** Smallest clone, in lines, that the pair ratchet tracks on its own (TODO_V5 PROC-07.a). */
export const LARGE_PAIR_LINES = 15;

const cloneLines = (d) => d.firstFile.end - d.firstFile.start + 1;
const relativeName = (name) => path.relative(process.cwd(), name).replace(/\\/g, '/');

/**
 * Whether a clone is only using or import lines (and braces): repeated headers, not repeated logic.
 * @param {string} fragment
 * @returns {boolean}
 */
export function isImportOnly(fragment) {
    return fragment.split('\n').every((line) => /^\s*(?:using\s|import\s|[{}()[\];,]*\s*$)/.test(line));
}

/**
 * The duplication the ratchet holds besides the 3+ families: clones of at least LARGE_PAIR_LINES lines
 * counted per pair of files, and duplicated lines per format. Import-only clones are left out.
 * @param {{format: string, fragment: string, firstFile: {name: string, start: number, end: number}, secondFile: {name: string}}[]} duplicates
 * @returns {{pairs: {files: string[], count: number}[], totals: Record<string, number>}}
 */
export function measureDuplication(duplicates) {
    const kept = duplicates.filter((d) => !isImportOnly(d.fragment ?? ''));
    const pairs = new Map();
    for (const d of kept.filter((c) => cloneLines(c) >= LARGE_PAIR_LINES)) {
        const files = [relativeName(d.firstFile.name), relativeName(d.secondFile.name)].sort();
        const key = files.join(' | ');
        pairs.set(key, { files, count: (pairs.get(key)?.count ?? 0) + 1 });
    }
    const totals = {};
    for (const d of kept) {
        totals[d.format] = (totals[d.format] ?? 0) + cloneLines(d);
    }
    return {
        pairs: [...pairs.values()].sort((a, b) => a.files.join().localeCompare(b.files.join())),
        totals: Object.fromEntries(Object.entries(totals).sort(([a], [b]) => a.localeCompare(b)))
    };
}

/**
 * Compares the large pairs and the per-format totals with the baseline: no new pair of files, no more
 * large clones per pair, no more duplicated lines per format, and a drop recorded in the baseline by the
 * commit that makes it (ratchet, A-03.b).
 * @param {{pairs: {files: string[], count: number}[], totals: Record<string, number>}} current
 * @param {{pairs?: {files: string[], count: number}[], totals?: Record<string, number>}} baseline
 * @returns {{errors: string[], warnings: string[]}}
 */
export function compareDuplication(current, baseline) {
    const errors = [];
    const warnings = [];
    const allowed = new Map((baseline.pairs ?? []).map((p) => [p.files.join(' | '), p.count]));
    const now = new Map(current.pairs.map((p) => [p.files.join(' | '), p.count]));
    for (const [key, count] of now) {
        if (count > (allowed.get(key) ?? 0)) {
            errors.push(`${count} clone(s) of ${LARGE_PAIR_LINES}+ lines between ${key}, allowed ${allowed.get(key) ?? 0}`);
        }
    }
    for (const [key, count] of allowed) {
        if ((now.get(key) ?? 0) < count) {
            errors.push(`duplication went down, shrink ${BASELINE_PATH} in the same commit: ${key}`);
        }
    }
    for (const format of new Set([...Object.keys(current.totals), ...Object.keys(baseline.totals ?? {})])) {
        const lines = current.totals[format] ?? 0;
        const limit = baseline.totals?.[format] ?? 0;
        if (lines > limit) {
            errors.push(`${lines} duplicated ${format} lines, baseline ${limit}`);
        } else if (lines < limit) {
            errors.push(`duplicated ${format} lines went down to ${lines}, shrink ${BASELINE_PATH} (${limit}) in the same commit`);
        }
    }
    return { errors, warnings };
}

function git(args) {
    return execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

function gitShowOrNull(spec) {
    try {
        return execFileSync('git', ['show', spec], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    } catch {
        return null;
    }
}

function runJscpd() {
    const out = mkdtempSync(path.join(tmpdir(), 'jscpd-'));
    try {
        execFileSync(process.execPath, [
            'node_modules/jscpd/run-jscpd.js', 'src',
            '--ignore', '**/bin/**,**/obj/**,**/*.Tests/**',
            '--reporters', 'json', '--output', out, '--silent'
        ], { stdio: 'ignore' });
        return JSON.parse(readFileSync(path.join(out, 'jscpd-report.json'), 'utf8')).duplicates;
    } finally {
        rmSync(out, { recursive: true, force: true });
    }
}

/**
 * Computes the current duplication, as stored in the baseline file: the 3+ clone families, the large
 * pairs and the duplicated lines per format, from one jscpd run.
 * @returns {{families: {files: string[], copies: number}[], pairs: {files: string[], count: number}[], totals: Record<string, number>}}
 */
export function currentDuplication() {
    const duplicates = runJscpd();
    const families = groupCloneFamilies(duplicates)
        .map((f) => ({ ...f, files: f.files.map(relativeName) }))
        .sort((a, b) => a.files.join().localeCompare(b.files.join()) || b.copies - a.copies);
    return { families, ...measureDuplication(duplicates) };
}

function main() {
    const errors = [];
    const warnings = [];
    const report = (list, file, items, what) => items.forEach((it) => list.push({ file, line: it.line, message: `${what}: ${it.match ?? ''}`.trim() }));

    for (const file of git(['ls-files']).split('\n').filter(Boolean)) {
        const ext = path.extname(file).toLowerCase();
        if (BINARY_EXTENSIONS.has(ext) || !existsSync(file)) {
            continue;
        }
        const text = readFileSync(file, 'utf8');
        findEmDashes(text).forEach((line) => errors.push({ file, line, message: 'em dash' }));
        if (file.startsWith(WEB_DIR) && ['.css', '.html', '.js'].includes(ext)) {
            report(errors, file, findColorLiterals(text, ext), 'color literal outside a var() fallback');
        }
        if (['.css', '.html', '.js', '.yml'].includes(ext)) {
            report(errors, file, findBannedFonts(text), 'banned font');
        }
        if (MARKER_EXTENSIONS.has(ext)) {
            report(errors, file, findUntrackedMarkers(text, ext), 'deferred-work comment without a registry id');
        }
    }

    checkTargetAbi({
        buildYaml: readFileSync('build.yaml', 'utf8'),
        manifest: readFileSync('manifest.json', 'utf8'),
        csproj: readFileSync('src/Jellyfin.Plugin.JuxHomepage/Jellyfin.Plugin.JuxHomepage.csproj', 'utf8')
    }).forEach((message) => errors.push({ file: 'build.yaml', message }));

    const rangeArg = process.argv.indexOf('--commits');
    const range = rangeArg > 0 ? process.argv[rangeArg + 1] : '';
    if (range) {
        const pushCommits = [];
        for (const sha of git(['rev-list', '--no-merges', range]).split('\n').filter(Boolean)) {
            const author = git(['log', '-1', '--format=%an', sha]).trim();
            if (author.endsWith('[bot]')) {
                continue;
            }
            const body = git(['log', '-1', '--format=%b', sha]);
            const added = {};
            for (const line of git(['diff-tree', '--no-commit-id', '--numstat', '-r', sha]).split('\n').filter(Boolean)) {
                const [plus, , file] = line.split('\t');
                added[file] = plus === '-' ? 0 : Number(plus);
            }
            const files = Object.keys(added);
            const result = checkCommit({ sha, subject: git(['log', '-1', '--format=%s', sha]).trim(), body, files, added });
            result.errors.forEach((message) => errors.push({ message }));
            result.warnings.forEach((message) => warnings.push({ message }));
            pushCommits.unshift({ sha, body, files });
        }
        const [base, head] = range.split('..');
        checkPushBudgets({
            base: git(['rev-parse', base]).trim(),
            head: git(['rev-parse', head || 'HEAD']).trim(),
            commits: pushCommits,
            show: gitShowOrNull
        }).forEach((message) => errors.push({ message }));
    }

    const baseline = existsSync(BASELINE_PATH) ? JSON.parse(readFileSync(BASELINE_PATH, 'utf8')) : {};
    const current = currentDuplication();
    for (const dup of [compareWithBaseline(current.families, baseline.families ?? []), compareDuplication(current, baseline)]) {
        dup.errors.forEach((message) => errors.push({ message }));
        dup.warnings.forEach((message) => warnings.push({ message }));
    }

    const annotate = (level, { file, line, message }) =>
        console.log(`::${level}${file ? ` file=${file}${line ? `,line=${line}` : ''}` : ''}::${message}`);
    warnings.forEach((w) => annotate('warning', w));
    errors.forEach((e) => annotate('error', e));
    console.log(`Static checks: ${errors.length} error(s), ${warnings.length} warning(s).`);
    process.exitCode = errors.length > 0 ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    if (process.argv.includes('--write-baseline')) {
        console.log(JSON.stringify(currentDuplication(), null, 2));
    } else {
        main();
    }
}
