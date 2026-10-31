import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { bumpVersion, lineBuildYaml, lineZipName, packageProblems, parseSha256List } from '../ci/plugin-package.mjs';

describe('bumpVersion', () => {
    it('bumps the part the release type names and resets the lower ones', () => {
        expect(bumpVersion('2.17.1', 'major')).toBe('3.0.0');
        expect(bumpVersion('2.17.1', 'minor')).toBe('2.18.0');
        expect(bumpVersion('2.17.1', 'patch')).toBe('2.17.2');
    });

    it('keeps the last version when no commit calls for a release', () => {
        expect(bumpVersion('2.17.1', null)).toBe('2.17.1');
    });

    it('gives 1.0.0 to a first release, as semantic-release does', () => {
        expect(bumpVersion(null, 'minor')).toBe('1.0.0');
        expect(bumpVersion(null, 'patch')).toBe('1.0.0');
        expect(bumpVersion(null, null)).toBe('0.0.0');
    });
});

const lines = [
    { name: '10.11', sdk: '10.11.9', framework: 'net9.0', targetAbi: '10.11.9.0', versionSuffix: '10' },
    { name: '12', sdk: '12.0.0', framework: 'net10.0', targetAbi: '12.0.0.0', versionSuffix: '12' }
];

describe('one archive per Jellyfin line (D-02)', () => {
    it('numbers each archive with the semver and the line as 4th component', () => {
        expect(lines.map((line) => lineZipName('2.18.0', line))).toEqual(['jellyux-homepage_2.18.0.10.zip', 'jellyux-homepage_2.18.0.12.zip']);
    });

    it('writes the build.yaml of a line from the versioned one, other fields kept', () => {
        const template = readFileSync('build.yaml', 'utf8');
        const yaml = lineBuildYaml(template, lines[1]);
        expect(yaml).toMatch(/^targetAbi: "12\.0\.0\.0"\r?$/m);
        expect(yaml).toMatch(/^framework: "net10\.0"\r?$/m);
        expect(yaml.replace(/^(targetAbi|framework):.*$/gm, '')).toBe(template.replace(/^(targetAbi|framework):.*$/gm, ''));
        expect(lineBuildYaml(template, lines[0])).toBe(template);
        expect(() => lineBuildYaml('name: "x"\n', lines[0])).toThrow();
    });

    it('reads the archive digests the package job passes on', () => {
        expect(parseSha256List('a.zip=abc b.zip=def')).toEqual({ 'a.zip': 'abc', 'b.zip': 'def' });
        expect(parseSha256List('')).toEqual({});
    });
});

describe('packageProblems', () => {
    const zips = [{ name: 'jellyux-homepage_2.18.0.10.zip', sha256: 'abc' }, { name: 'jellyux-homepage_2.18.0.12.zip', sha256: 'def' }];
    const tested = {
        version: '2.18.0',
        expectedVersion: '2.18.0',
        zips,
        expectedSha256: { 'jellyux-homepage_2.18.0.10.zip': 'abc', 'jellyux-homepage_2.18.0.12.zip': 'def' },
        lines
    };

    it('accepts the archives the CI tested, at the version semantic-release computed', () => {
        expect(packageProblems(tested)).toEqual([]);
    });

    it('refuses a version moved by a release approved since the package job', () => {
        expect(packageProblems({ ...tested, version: '2.19.0' })).toEqual([
            'semantic-release computed 2.19.0, the tested archives were built as 2.18.0: rerun the workflow.'
        ]);
    });

    it('refuses another archive, a missing line or an extra zip', () => {
        expect(packageProblems({ ...tested, zips: [zips[0], { ...zips[1], sha256: 'xyz' }] })).toEqual([
            'jellyux-homepage_2.18.0.12.zip has sha256 xyz, the tested archive def.'
        ]);
        expect(packageProblems({ ...tested, zips: [zips[0]] })).toEqual([
            'artifacts/ holds jellyux-homepage_2.18.0.10.zip, expected one zip per Jellyfin line: jellyux-homepage_2.18.0.10.zip, jellyux-homepage_2.18.0.12.zip.'
        ]);
        expect(packageProblems({ ...tested, zips: [...zips, { name: 'b.zip', sha256: 'abc' }] })).toHaveLength(1);
    });

    it('refuses to publish when the package job outputs are missing', () => {
        expect(packageProblems({ ...tested, expectedVersion: '', expectedSha256: {} })).toHaveLength(3);
    });
});
