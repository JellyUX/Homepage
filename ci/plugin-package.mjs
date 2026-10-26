// The jprm archive the E2E installs is the one a release publishes. Two commands, run
// from the repository root:
//   node ci/plugin-package.mjs next-version
//     prints "release=true|false" and "version=X.Y.Z" (GITHUB_OUTPUT lines): the version semantic-release
//     would give HEAD, from the last v* tag and the commit analyzer of .releaserc.json (same rules), or
//     the last version when no commit calls for a release. The package job builds the archive with it.
//   node ci/plugin-package.mjs verify <version>
//     the verifyReleaseCmd of .releaserc.json: fails unless ./artifacts holds one zip whose sha256 is
//     PACKAGE_SHA256 and the version semantic-release computed is PACKAGE_VERSION, the archive the E2E
//     installed. A run whose commit is behind main never gets here: semantic-release skips it ("local
//     branch is behind the remote one") and the newer run carries its commits.

import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
 * @param {{version: string, expectedVersion: string, zips: {name: string, sha256: string}[], expectedSha256: string}} input
 * @returns {string[]} what keeps the archive from being published, empty when it is the tested one
 */
export function packageProblems({ version, expectedVersion, zips, expectedSha256 }) {
    const problems = [];
    if (version !== expectedVersion) {
        problems.push(`semantic-release computed ${version}, the tested archive was built as ${expectedVersion || '(none)'}: rerun the workflow.`);
    }
    if (zips.length !== 1) {
        problems.push(`expected one zip in artifacts/, found ${zips.length}.`);
    } else if (zips[0].sha256 !== expectedSha256) {
        problems.push(`${zips[0].name} has sha256 ${zips[0].sha256}, the tested archive ${expectedSha256 || '(none)'}.`);
    }
    return problems;
}

const git = (args) => execFileSync('git', args, { encoding: 'utf8' });

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

function verify(version) {
    const zips = readdirSync('artifacts').filter((name) => name.endsWith('.zip')).map((name) => ({
        name,
        sha256: createHash('sha256').update(readFileSync(path.join('artifacts', name))).digest('hex')
    }));
    zips.forEach((zip) => console.log(`${zip.sha256}  artifacts/${zip.name}`));
    const problems = packageProblems({
        version,
        expectedVersion: process.env.PACKAGE_VERSION ?? '',
        zips,
        expectedSha256: process.env.PACKAGE_SHA256 ?? ''
    });
    problems.forEach((problem) => console.error(`::error::${problem}`));
    process.exitCode = problems.length > 0 ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const [command, version] = process.argv.slice(2);
    if (command === 'next-version') {
        await nextVersion();
    } else if (command === 'verify' && version) {
        verify(version);
    } else {
        console.error('Usage: node ci/plugin-package.mjs next-version | verify <version>');
        process.exitCode = 2;
    }
}
