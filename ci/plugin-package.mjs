// The jprm archives the CI tests are the ones a release publishes: one per Jellyfin line of
// ci/jellyfin-lines.json (D-01), numbered X.Y.Z.<versionSuffix> (D-02). Three commands, run from the
// repository root:
//   node ci/plugin-package.mjs next-version
//     prints "release=true|false" and "version=X.Y.Z" (GITHUB_OUTPUT lines): the version semantic-release
//     would give HEAD, from the last v* tag and the commit analyzer of .releaserc.json (same rules), or
//     the last version when no commit calls for a release. The package job builds the archives with it.
//   node ci/plugin-package.mjs build <version>
//     builds one archive per line into ./artifacts: the line's build.yaml (targetAbi, framework), the
//     assembly version X.Y.Z.<suffix> and the line's SDK (JellyfinVersion), then restores build.yaml and
//     the .csproj. Prints "sha256=<zip>=<digest> ..." (GITHUB_OUTPUT line).
//   node ci/plugin-package.mjs verify <version>
//     the verifyReleaseCmd of .releaserc.json: fails unless ./artifacts holds one zip per line, each with
//     the sha256 PACKAGE_SHA256 lists for it, and the version semantic-release computed is
//     PACKAGE_VERSION. A run whose commit is behind main never gets here: semantic-release skips it
//     ("local branch is behind the remote one") and the newer run carries its commits.

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const LINES_FILE = 'ci/jellyfin-lines.json';
const BUILD_YAML = 'build.yaml';
const CSPROJ = 'src/Jellyfin.Plugin.JuxHomepage/Jellyfin.Plugin.JuxHomepage.csproj';
const ZIP_SLUG = 'jellyux-homepage';
// jprm cleans before it restores: the assets of the previous line's framework would fail that clean.
const OBJ_DIRS = ['src/Jellyfin.Plugin.JuxHomepage/obj', 'src/Jellyfin.Plugin.JuxHomepage.Tests/obj'];

/**
 * @param {string | null} last last released version, X.Y.Z, or null before the first release
 * @param {'major' | 'minor' | 'patch' | null} type release type of the commits since it
 * @returns {string}
 */
export function bumpVersion(last, type) {
    if (last === null) {
        // semantic-release makes the first release 1.0.0, whatever the type.
        return type ? '1.0.0' : '0.0.0';
    }
    const [major, minor, patch] = last.split('.').map(Number);
    if (type === 'major') {
        return `${major + 1}.0.0`;
    }
    if (type === 'minor') {
        return `${major}.${minor + 1}.0`;
    }
    if (type === 'patch') {
        return `${major}.${minor}.${patch + 1}`;
    }
    return last;
}

/**
 * @param {string} version X.Y.Z
 * @param {{versionSuffix: string}} line
 * @returns {string} The archive jprm builds for the line.
 */
export function lineZipName(version, line) {
    return `${ZIP_SLUG}_${version}.${line.versionSuffix}.zip`;
}

/**
 * The build.yaml of one Jellyfin line: the versioned one (the default line) with the line's targetAbi and
 * framework, every other field kept.
 * @param {string} template
 * @param {{targetAbi: string, framework: string}} line
 * @returns {string}
 */
export function lineBuildYaml(template, line) {
    let yaml = template;
    for (const [key, value] of [['targetAbi', line.targetAbi], ['framework', line.framework]]) {
        const field = new RegExp(`^${key}:.*?(?=\\r?$)`, 'm');
        if (!field.test(yaml)) {
            throw new Error(`${BUILD_YAML} has no ${key} line.`);
        }
        yaml = yaml.replace(field, `${key}: "${value}"`);
    }
    return yaml;
}

/**
 * @param {string} list "<zip>=<sha256> ..." as the package job outputs it
 * @returns {Record<string, string>}
 */
export function parseSha256List(list) {
    return Object.fromEntries(list.split(/\s+/).filter(Boolean).map((entry) => entry.split('=')));
}

/**
 * @param {{version: string, expectedVersion: string, zips: {name: string, sha256: string}[],
 *   expectedSha256: Record<string, string>, lines: {versionSuffix: string}[]}} input
 * @returns {string[]} what keeps the archives from being published, empty when they are the tested ones
 */
export function packageProblems({ version, expectedVersion, zips, expectedSha256, lines }) {
    const problems = [];
    if (version !== expectedVersion) {
        problems.push(`semantic-release computed ${version}, the tested archives were built as ${expectedVersion || '(none)'}: rerun the workflow.`);
    }
    const expectedNames = lines.map((line) => lineZipName(expectedVersion || version, line));
    const names = zips.map((zip) => zip.name).sort();
    if (names.join() !== [...expectedNames].sort().join()) {
        problems.push(`artifacts/ holds ${names.join(', ') || 'no zip'}, expected one zip per Jellyfin line: ${expectedNames.join(', ')}.`);
    } else {
        for (const zip of zips.filter((z) => z.sha256 !== expectedSha256[z.name])) {
            problems.push(`${zip.name} has sha256 ${zip.sha256}, the tested archive ${expectedSha256[zip.name] || '(none)'}.`);
        }
    }
    return problems;
}

const git = (args) => execFileSync('git', args, { encoding: 'utf8' });
const readLines = () => JSON.parse(readFileSync(LINES_FILE, 'utf8')).lines;
const sha256Of = (file) => createHash('sha256').update(readFileSync(file)).digest('hex');

async function nextVersion() {
    const lastTag = git(['tag', '--merged', 'HEAD', '--list', 'v*', '--sort=-v:refname']).split('\n')[0].trim();
    const last = lastTag ? lastTag.slice(1) : null;
    const log = git(['log', '--format=%H%x00%B%x1e', lastTag ? `${lastTag}..HEAD` : 'HEAD']);
    const commits = log.split('\x1e').map((entry) => entry.trim()).filter(Boolean).map((entry) => {
        const [hash, message] = entry.split('\x00');
        return { hash, message };
    });
    const releaserc = JSON.parse(readFileSync('.releaserc.json', 'utf8'));
    const analyzer = releaserc.plugins.find((plugin) => [plugin].flat()[0] === '@semantic-release/commit-analyzer');
    const { analyzeCommits } = await import('@semantic-release/commit-analyzer');
    const silent = { log: () => {} };
    const type = await analyzeCommits([analyzer].flat()[1] ?? {}, { commits, logger: silent, cwd: process.cwd() });
    console.log(`release=${Boolean(type)}`);
    console.log(`version=${bumpVersion(last, type)}`);
}

function build(version) {
    const template = readFileSync(BUILD_YAML, 'utf8');
    const csproj = readFileSync(CSPROJ, 'utf8');
    const digests = [];
    try {
        for (const line of readLines()) {
            const fullVersion = `${version}.${line.versionSuffix}`;
            writeFileSync(BUILD_YAML, lineBuildYaml(template, line));
            OBJ_DIRS.forEach((dir) => rmSync(dir, { recursive: true, force: true }));
            execFileSync('python3', ['.github/scripts/set-plugin-version.py', fullVersion], { stdio: ['ignore', 2, 2] });
            // MSBuild reads JellyfinVersion from the environment: the line's SDK and target framework.
            execFileSync('jprm', ['plugin', 'build', '.', `--version=${fullVersion}`, '--output=./artifacts'], {
                stdio: ['ignore', 2, 2],
                env: { ...process.env, JellyfinVersion: line.sdk }
            });
            const zip = lineZipName(version, line);
            digests.push(`${zip}=${sha256Of(path.join('artifacts', zip))}`);
            console.error(`Jellyfin ${line.name}: artifacts/${zip}`);
        }
    } finally {
        writeFileSync(BUILD_YAML, template);
        writeFileSync(CSPROJ, csproj);
        // The last line's restore assets would break a later --no-restore build of the default line.
        OBJ_DIRS.forEach((dir) => rmSync(dir, { recursive: true, force: true }));
    }
    console.log(`sha256=${digests.join(' ')}`);
}

function verify(version) {
    const zips = readdirSync('artifacts').filter((name) => name.endsWith('.zip')).map((name) => ({
        name,
        sha256: sha256Of(path.join('artifacts', name))
    }));
    zips.forEach((zip) => console.log(`${zip.sha256}  artifacts/${zip.name}`));
    const problems = packageProblems({
        version,
        expectedVersion: process.env.PACKAGE_VERSION ?? '',
        zips,
        expectedSha256: parseSha256List(process.env.PACKAGE_SHA256 ?? ''),
        lines: readLines()
    });
    problems.forEach((problem) => console.error(`::error::${problem}`));
    process.exitCode = problems.length > 0 ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const [command, version] = process.argv.slice(2);
    if (command === 'next-version') {
        await nextVersion();
    } else if (command === 'build' && version) {
        build(version);
    } else if (command === 'verify' && version) {
        verify(version);
    } else {
        console.error('Usage: node ci/plugin-package.mjs next-version | build <version> | verify <version>');
        process.exitCode = 2;
    }
}
