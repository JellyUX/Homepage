import { describe, it, expect } from 'vitest';
import { bumpVersion, packageProblems } from '../ci/plugin-package.mjs';

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

describe('packageProblems', () => {
    const tested = { version: '2.18.0', expectedVersion: '2.18.0', zips: [{ name: 'a.zip', sha256: 'abc' }], expectedSha256: 'abc' };

    it('accepts the archive the E2E installed, at the version semantic-release computed', () => {
        expect(packageProblems(tested)).toEqual([]);
    });

    it('refuses a version moved by a release approved since the package job', () => {
        expect(packageProblems({ ...tested, version: '2.19.0' })).toEqual([
            'semantic-release computed 2.19.0, the tested archive was built as 2.18.0: rerun the workflow.'
        ]);
    });

    it('refuses another archive, none or several', () => {
        expect(packageProblems({ ...tested, zips: [{ name: 'a.zip', sha256: 'def' }] })).toEqual(['a.zip has sha256 def, the tested archive abc.']);
        expect(packageProblems({ ...tested, zips: [] })).toEqual(['expected one zip in artifacts/, found 0.']);
        expect(packageProblems({ ...tested, zips: [...tested.zips, { name: 'b.zip', sha256: 'abc' }] })).toHaveLength(1);
    });

    it('refuses to publish when the package job outputs are missing', () => {
        expect(packageProblems({ ...tested, expectedVersion: '', expectedSha256: '' })).toHaveLength(2);
    });
});
