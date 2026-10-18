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
export const BUDGET_FILES = ['ci/perf-budget.json', 'ci/asset-budget.json'];

const HEX_COLOR = /(?<![\w&#-])#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\w-])/g;
const COLOR_FUNCTION = /\b(?:rgba?|hsla?)\(\s*[\d.]/g;
const VAR_FALLBACK = /var\(\s*--[\w-]+\s*,\s*(?:#[0-9a-fA-F]{3,8}|(?:rgba?|hsla?)\([^()]*\))\s*\)/g;
// A comment that defers work must name the registry id that tracks it (TODO_V5 rule R8, PROC-04.b).
const DEFERRED_MARKER = /\bTODO\b|\bFIXME\b|future phase|worth revisiting/gi;
const REGISTRY_ID = /\b[A-Z]{1,5}-\d{2}\b/;
const MARKER_EXTENSIONS = new Set(['.cs', '.js', '.mjs', '.css', '.html']);

/**
 * Blanks out comments while keeping line breaks, so reported line numbers stay right.
 * @param {string} text
 * @param {string} ext File extension, including the dot.
 * @returns {string}
 */
export function stripComments(text, ext) {
    const blank = (m) => m.replace(/[^\n]/g, ' ');
    let out = text.replace(/\/\*[\s\S]*?\*\//g, blank);
    if (ext === '.html') {
        out = out.replace(/<!--[\s\S]*?-->/g, blank);
    }
    if (ext === '.js' || ext === '.mjs' || ext === '.html' || ext === '.cs') {
        out = out.replace(/(^|[^:\\'"`])\/\/[^\n]*/g, (m, lead) => lead + blank(m.slice(lead.length)));
    }
    return out;
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
 * @param {{sha: string, subject: string, body: string, files: string[]}} commit
 * @returns {{errors: string[], warnings: string[]}}
 */
export function checkCommit({ sha, subject, body, files }) {
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
        const touchesWeb = files.some((f) => f.startsWith(WEB_DIR));
        const touchesTests = files.some((f) => f.startsWith('test/') || f.startsWith('e2e/') || f.includes('.Tests/'));
        if (touchesWeb && !touchesTests && !/^untestable:\s*\S/im.test(body)) {
            errors.push(`${short}: a fix: on web assets without a test needs an "Untestable: <reason>" trailer.`);
        }
        if (files.length > 3 && body.trim() === '') {
            warnings.push(`${short}: a fix: touching ${files.length} files has no body explaining it.`);
        }
    }
    return { errors, warnings };
}

/**
 * Checks that a commit raising a ratcheted budget says why in a "Budget: <reason>" trailer.
 * Only the numeric entries of the budget's "endpoints" object are compared; a new entry has no
 * previous value to raise, so it needs no trailer.
 * @param {{sha: string, file: string, before: string | null, after: string | null, body: string}} change
 *   The budget file's text before and after the commit (null when absent).
 * @returns {string[]} Errors.
 */
export function checkBudget({ sha, file, before, after, body }) {
    if (after === null || /^budget:\s*\S/im.test(body)) {
        return [];
    }
    // Every numeric value of every top-level table (endpoints, assets), keyed by table and name.
    const entries = (text) => Object.fromEntries(Object.entries(text === null ? {} : JSON.parse(text))
        .filter(([, table]) => table !== null && typeof table === 'object')
        .flatMap(([table, values]) => Object.entries(values).map(([key, value]) => [`${table}/${key}`, value])));
    const previous = entries(before);
    const raised = Object.entries(entries(after))
        .filter(([key, value]) => typeof previous[key] === 'number' && typeof value === 'number' && value > previous[key])
        .map(([key, value]) => `"${key}" ${previous[key]} -> ${value}`);
    return raised.length === 0
        ? []
        : [`${sha.slice(0, 7)}: ${file} raises ${raised.join(', ')} without a "Budget: <reason>" trailer.`];
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
            warnings.push(`duplication went down, shrink ${BASELINE_PATH}: ${key}`);
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
 * Computes the current clone families, as stored in the baseline file.
 * @returns {{files: string[], copies: number}[]}
 */
export function currentCloneFamilies() {
    return groupCloneFamilies(runJscpd())
        .map((f) => ({ ...f, files: f.files.map((name) => path.relative(process.cwd(), name).replace(/\\/g, '/')) }))
        .sort((a, b) => a.files.join().localeCompare(b.files.join()) || b.copies - a.copies);
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
        for (const sha of git(['rev-list', '--no-merges', range]).split('\n').filter(Boolean)) {
            const author = git(['log', '-1', '--format=%an', sha]).trim();
            if (author.endsWith('[bot]')) {
                continue;
            }
            const body = git(['log', '-1', '--format=%b', sha]);
            const files = git(['diff-tree', '--no-commit-id', '--name-only', '-r', sha]).split('\n').filter(Boolean);
            const result = checkCommit({ sha, subject: git(['log', '-1', '--format=%s', sha]).trim(), body, files });
            result.errors.forEach((message) => errors.push({ message }));
            result.warnings.forEach((message) => warnings.push({ message }));
            for (const file of BUDGET_FILES.filter((f) => files.includes(f))) {
                const before = gitShowOrNull(`${sha}^:${file}`);
                const after = gitShowOrNull(`${sha}:${file}`);
                checkBudget({ sha, file, before, after, body }).forEach((message) => errors.push({ file, message }));
            }
        }
    }

    const baseline = existsSync(BASELINE_PATH) ? JSON.parse(readFileSync(BASELINE_PATH, 'utf8')) : [];
    const dup = compareWithBaseline(currentCloneFamilies(), baseline);
    dup.errors.forEach((message) => errors.push({ message }));
    dup.warnings.forEach((message) => warnings.push({ message }));

    const annotate = (level, { file, line, message }) =>
        console.log(`::${level}${file ? ` file=${file}${line ? `,line=${line}` : ''}` : ''}::${message}`);
    warnings.forEach((w) => annotate('warning', w));
    errors.forEach((e) => annotate('error', e));
    console.log(`Static checks: ${errors.length} error(s), ${warnings.length} warning(s).`);
    process.exitCode = errors.length > 0 ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    if (process.argv.includes('--write-baseline')) {
        console.log(JSON.stringify(currentCloneFamilies(), null, 2));
    } else {
        main();
    }
}
