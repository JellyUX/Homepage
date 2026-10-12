import { describe, it, expect } from 'vitest';
import {
    findEmDashes,
    findColorLiterals,
    findBannedFonts,
    checkTargetAbi,
    checkCommit,
    groupCloneFamilies,
    compareWithBaseline
} from '../ci/static-checks.mjs';

describe('findEmDashes', () => {
    it('reports the lines holding an em dash', () => {
        const dash = String.fromCharCode(0x2014);
        expect(findEmDashes(`a - b\nc ${dash} d\ne`)).toEqual([2]);
    });
});

describe('findColorLiterals', () => {
    it('reports hex and rgb literals', () => {
        const css = '.a { color: #fff; }\n.b { background: rgba(0, 0, 0, 0.5); }';
        expect(findColorLiterals(css, '.css').map((f) => f.line)).toEqual([1, 2]);
    });

    it('accepts literals used as var() fallbacks', () => {
        const css = '.a { color: var(--error-color, #e5350b); border: 1px solid var(--borderColor, rgba(255, 255, 255, 0.1)); }';
        expect(findColorLiterals(css, '.css')).toEqual([]);
    });

    it('accepts colors built from palette channel variables', () => {
        expect(findColorLiterals('.a { background: rgb(var(--jf-palette-text-primaryChannel) / 0.15); }', '.css')).toEqual([]);
    });

    it('ignores comments and keeps line numbers', () => {
        const css = '/* brand blue\n * is #00a4dc */\n.a { color: #123456; }';
        expect(findColorLiterals(css, '.css')).toEqual([{ line: 3, match: '#123456' }]);
    });

    it('does not mistake ids or HTML entities for colors', () => {
        const js = "document.querySelector('#jux-tab-history'); el.innerHTML = '&#215;';";
        expect(findColorLiterals(js, '.js')).toEqual([]);
    });

    it('keeps URLs while dropping line comments in scripts', () => {
        const js = "var u = 'http://x/y'; // #abcdef\nel.style.color = '#abcdef';";
        expect(findColorLiterals(js, '.js')).toEqual([{ line: 2, match: '#abcdef' }]);
    });
});

describe('findBannedFonts', () => {
    it('reports a banned font family', () => {
        const font = ['Plus', 'Jakarta', 'Sans'].join(' ');
        expect(findBannedFonts(`font-family: '${font}', sans-serif;`)).toEqual([{ line: 1, match: font }]);
    });

    it('matches whole words only', () => {
        expect(findBannedFonts('setInterval(fn, 10); // International')).toEqual([]);
    });
});

describe('checkTargetAbi', () => {
    const manifest = (versions) => JSON.stringify([{ versions }]);
    const ok = {
        buildYaml: 'targetAbi: "10.11.9.0"\n',
        manifest: manifest([{ version: '2.3.0.0', targetAbi: '10.11.0.0' }, { version: '2.4.0.0', targetAbi: '10.11.9.0' }]),
        ciYml: 'run: dotnet build x.csproj -p:JellyfinVersion=10.11.9'
    };

    it('passes when build.yaml, the manifest and the CI minimum build agree', () => {
        expect(checkTargetAbi(ok)).toEqual([]);
    });

    it('fails when a version calling GetUsers declares an older ABI', () => {
        const errors = checkTargetAbi({ ...ok, manifest: manifest([{ version: '2.5.0.0', targetAbi: '10.11.0.0' }]) });
        expect(errors).toHaveLength(1);
    });

    it('fails when the CI minimum build does not match targetAbi', () => {
        expect(checkTargetAbi({ ...ok, ciYml: '-p:JellyfinVersion=10.11.10' })).toHaveLength(1);
    });
});

describe('checkCommit', () => {
    const commit = (over) => ({ sha: 'abcdef0123', subject: 'fix: x', body: '', files: ['src/Foo.cs'], ...over });

    it('accepts a Conventional Commit', () => {
        expect(checkCommit(commit({ subject: 'chore(release): 2.10.0 [skip ci]' }))).toEqual({ errors: [], warnings: [] });
    });

    it('rejects an unknown type and a co-author trailer', () => {
        const { errors } = checkCommit(commit({ subject: 'update stuff', body: 'Co-Authored-By: X <x@y>' }));
        expect(errors).toHaveLength(2);
    });

    it('requires an Untestable trailer on a web fix without a test', () => {
        const web = ['src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js'];
        expect(checkCommit(commit({ files: web })).errors).toHaveLength(1);
        expect(checkCommit(commit({ files: web, body: 'Untestable: needs a live bundle' })).errors).toEqual([]);
        expect(checkCommit(commit({ files: [...web, 'test/jux-ui.test.js'] })).errors).toEqual([]);
    });

    it('warns on a large fix without a body', () => {
        expect(checkCommit(commit({ files: ['a', 'b', 'c', 'd'] })).warnings).toHaveLength(1);
    });
});

describe('duplication ratchet', () => {
    const loc = (name, start) => ({ name, start, end: start + 10 });
    const pair = (a, b) => ({ firstFile: a, secondFile: b });

    it('groups overlapping clone pairs into families of 3 or more', () => {
        const families = groupCloneFamilies([
            pair(loc('a.js', 1), loc('b.js', 1)),
            pair(loc('b.js', 3), loc('c.js', 1)),
            pair(loc('d.js', 1), loc('e.js', 1))
        ]);
        expect(families).toEqual([{ files: ['a.js', 'b.js', 'c.js'], copies: 3 }]);
    });

    it('fails on a new family or an extra copy, and warns when duplication went down', () => {
        const baseline = [{ files: ['a.js', 'b.js', 'c.js'], copies: 3 }, { files: ['x.js', 'y.js', 'z.js'], copies: 3 }];
        expect(compareWithBaseline([{ files: ['a.js', 'b.js', 'c.js'], copies: 4 }], baseline).errors).toHaveLength(1);
        expect(compareWithBaseline([{ files: ['d.js', 'e.js', 'f.js'], copies: 3 }], baseline).errors).toHaveLength(1);
        const shrunk = compareWithBaseline([{ files: ['a.js', 'b.js', 'c.js'], copies: 3 }], baseline);
        expect(shrunk).toEqual({ errors: [], warnings: [expect.stringContaining('x.js')] });
    });
});
