import { describe, it, expect } from 'vitest';
import {
    findEmDashes,
    findColorLiterals,
    findBannedFonts,
    findUntrackedMarkers,
    checkTargetAbi,
    checkCommit,
    checkBudget,
    checkPushBudgets,
    groupCloneFamilies,
    compareWithBaseline,
    isImportOnly,
    measureDuplication,
    compareDuplication
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

describe('findUntrackedMarkers', () => {
    it('reports a marker comment that names no registry id', () => {
        const cs = 'var a = 1; // TODO: cache this\n/// <summary>Worth revisiting later.</summary>\n// FIXME (PERF-01): one rebuild';
        expect(findUntrackedMarkers(cs, '.cs')).toEqual([{ line: 1, match: 'TODO' }, { line: 2, match: 'Worth revisiting' }]);
    });

    it('accepts a marker that names a registry id on the same line', () => {
        expect(findUntrackedMarkers('/* a later phase, see H-09, worth revisiting */', '.js')).toEqual([]);
    });

    it('ignores code, strings and roadmap file names', () => {
        const js = "const label = 'TODO'; // per TODO_V4.md 8.3\nfixme();";
        expect(findUntrackedMarkers(js, '.js')).toEqual([]);
    });

    it('keeps positions after a character outside the basic plane', () => {
        expect(findUntrackedMarkers('const s = "\u{1F600}"; const label = "TODO";', '.js')).toEqual([]);
    });

    it('matches the markers whatever their case', () => {
        expect(findUntrackedMarkers('// todo: Worth Revisiting in a Future Phase', '.js').map((f) => f.match))
            .toEqual(['todo', 'Worth Revisiting', 'Future Phase']);
    });

    it('takes only the registry prefixes as an id (A-03.e)', () => {
        expect(findUntrackedMarkers('// TODO: decode as UTF-16 first', '.js')).toEqual([{ line: 1, match: 'TODO' }]);
        expect(findUntrackedMarkers('// TODO (A-15): measure the client keys only', '.js')).toEqual([]);
    });

    it('reads a comment after a string holding a double slash, and no comment inside one (A-03.e)', () => {
        expect(findUntrackedMarkers("const u = 'see // TODO later';", '.js')).toEqual([]);
        expect(findUntrackedMarkers('const u = "a:b//c"; // TODO cache', '.js')).toEqual([{ line: 1, match: 'TODO' }]);
        expect(findUntrackedMarkers('const t = `x // TODO ${y}`;', '.mjs')).toEqual([]);
        expect(findUntrackedMarkers("if (/['\"]/.test(s)) { } // FIXME quotes", '.js')).toEqual([{ line: 1, match: 'FIXME' }]);
        expect(findUntrackedMarkers('var p = @"C:\\dir\\"; // TODO path', '.cs')).toEqual([{ line: 1, match: 'TODO' }]);
        expect(findUntrackedMarkers('var s = "/* not a comment */ TODO";', '.cs')).toEqual([]);
    });

    it('takes a per-phase decision as a registry id (R11)', () => {
        expect(findUntrackedMarkers('// TODO (DEC-P12-6): later', '.js')).toEqual([]);
    });

    it('reads a division after an increment, C# raw strings and @$ strings (R11)', () => {
        expect(findUntrackedMarkers('var r = i++ / 2; // TODO real', '.js')).toEqual([{ line: 1, match: 'TODO' }]);
        expect(findUntrackedMarkers('var q = """\n  // TODO not a comment\n  """; // TODO real', '.cs')).toEqual([{ line: 3, match: 'TODO' }]);
        expect(findUntrackedMarkers('var p = @$"C:\\dir\\"; // TODO real', '.cs')).toEqual([{ line: 1, match: 'TODO' }]);
    });

    it('reads future phase in a multi-line block comment', () => {
        // Split so that this file's own block-comment-looking string does not trip the check.
        expect(findUntrackedMarkers('/*\n * in a future' + ' phase\n */', '.css')).toEqual([{ line: 2, match: 'future phase' }]);
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
        csproj: `<JellyfinVersion Condition="'$(JellyfinVersion)' == ''">10.11.9</JellyfinVersion>`
    };

    it('passes when build.yaml, the manifest and the SDK version agree', () => {
        expect(checkTargetAbi(ok)).toEqual([]);
    });

    it('fails when a version calling GetUsers declares an older ABI', () => {
        const errors = checkTargetAbi({ ...ok, manifest: manifest([{ version: '2.5.0.0', targetAbi: '10.11.0.0' }]) });
        expect(errors).toHaveLength(1);
    });

    it('fails when the plugin compiles against a newer SDK than targetAbi', () => {
        expect(checkTargetAbi({ ...ok, csproj: `<JellyfinVersion Condition="'$(JellyfinVersion)' == ''">10.11.10</JellyfinVersion>` })).toHaveLength(1);
    });
});

describe('checkBudget', () => {
    const budget = (endpoints) => JSON.stringify({ description: 'x', endpoints });
    const change = (over) => ({
        sha: 'abcdef0123',
        file: 'ci/perf-budget.json',
        before: budget({ Sections: 17, 'Watchlist/Ids': 1 }),
        after: budget({ Sections: 17, 'Watchlist/Ids': 1 }),
        body: '',
        ...over
    });

    it('rejects a raised value without a Budget trailer, naming it', () => {
        const errors = checkBudget(change({ after: budget({ Sections: 18, 'Watchlist/Ids': 1 }) }));
        expect(errors).toHaveLength(1);
        expect(errors[0]).toContain('"endpoints/Sections" 17 -> 18');
    });

    it('reads every budget table, the asset sizes included', () => {
        const assets = (size) => JSON.stringify({ description: 'x', assets: { 'jux-core.js': size } });
        const raised = checkBudget(change({ file: 'ci/asset-budget.json', before: assets(4140), after: assets(4300) }));
        expect(raised).toEqual([expect.stringContaining('"assets/jux-core.js" 4140 -> 4300')]);
        expect(checkBudget(change({ file: 'ci/asset-budget.json', before: assets(4140), after: assets(4000) }))).toEqual([]);
    });

    it('accepts a raised value with a Budget trailer', () => {
        expect(checkBudget(change({ after: budget({ Sections: 18 }), body: 'Budget: a new section type' }))).toEqual([]);
    });

    it('accepts a lowered value and a new entry without a trailer', () => {
        expect(checkBudget(change({ after: budget({ Sections: 9, 'Watchlist/Ids': 1, 'Collections/IncludedIn': 3 }) }))).toEqual([]);
    });

    it('rejects a longer allowlist and more clones per set of files, a new set included', () => {
        const allowlist = (names) => JSON.stringify({ description: 'x', csharp: Object.fromEntries(names.map((n) => [n, 'route'])) });
        const longer = checkBudget(change({ file: 'ci/dead-code-allowlist.json', before: allowlist(['A']), after: allowlist(['A', 'B']) }));
        expect(longer).toEqual([expect.stringContaining('"csharp/#entries" 1 -> 2')]);
        expect(checkBudget(change({ file: 'ci/dead-code-allowlist.json', before: allowlist(['A', 'B']), after: allowlist(['A']) }))).toEqual([]);

        const baseline = (pairs) => JSON.stringify({ families: [], pairs, totals: { csharp: 10 } });
        const ab = { files: ['A.cs', 'B.cs'], count: 1 };
        const more = checkBudget(change({ file: 'ci/duplication-baseline.json', before: baseline([ab]), after: baseline([ab, { files: ['A.cs', 'C.cs'], count: 1 }]) }));
        expect(more).toEqual([expect.stringContaining('"pairs/A.cs | C.cs" 0 -> 1')]);
    });

    it('needs no trailer for a table the file did not have before', () => {
        const before = JSON.stringify([{ files: ['a.js'], copies: 3 }]);
        const after = JSON.stringify({ families: [{ files: ['a.js'], copies: 3 }], pairs: [{ files: ['A.cs', 'B.cs'], count: 2 }], totals: { csharp: 9 } });
        expect(checkBudget(change({ file: 'ci/duplication-baseline.json', before, after }))).toEqual([]);
    });

    it('accepts the commit that creates or deletes the budget', () => {
        expect(checkBudget(change({ before: null }))).toEqual([]);
        expect(checkBudget(change({ after: null }))).toEqual([]);
    });
});

describe('checkPushBudgets', () => {
    const file = 'ci/perf-budget.json';
    const budget = (sections) => JSON.stringify({ description: 'x', endpoints: { Sections: sections } });
    // Each commit names the budget text it leaves (null: deleted); "base" is the text before the push.
    const run = (base, commits) => {
        const texts = { 'base': base };
        commits.forEach((c) => { texts[c.sha] = c.text; });
        const parent = (sha) => (sha === commits[0].sha ? 'base' : commits[commits.findIndex((c) => c.sha === sha) - 1].sha);
        const show = (spec) => {
            const [rev] = spec.split(':');
            const key = rev.endsWith('^') ? parent(rev.slice(0, -1)) : rev;
            return texts[key] ?? null;
        };
        return checkPushBudgets({
            base: 'base',
            head: commits.at(-1).sha,
            commits: commits.map((c) => ({ sha: c.sha, body: c.body ?? '', files: [file] })),
            show
        });
    };

    it('rejects a budget deleted then put back higher in two commits (A-03.a)', () => {
        const errors = run(budget(17), [{ sha: 'aaaaaaa1', text: null }, { sha: 'bbbbbbb2', text: budget(20) }]);
        expect(errors).toEqual([expect.stringContaining('"endpoints/Sections" 17 -> 20')]);
    });

    it('accepts the push when one of its commits says why the budget rose', () => {
        expect(run(budget(17), [{ sha: 'aaaaaaa1', text: null }, { sha: 'bbbbbbb2', text: budget(20), body: 'Budget: a new type' }])).toEqual([]);
    });

    it('does not let a trailer of an unrelated raise cover a delete-then-restore (R11)', () => {
        const errors = run(budget(17), [
            { sha: 'aaaaaaa1', text: budget(18), body: 'Budget: a new type' },
            { sha: 'bbbbbbb2', text: null },
            { sha: 'ccccccc3', text: budget(40) }
        ]);
        expect(errors).toEqual([expect.stringContaining('"endpoints/Sections" 17 -> 40')]);
    });

    it('reports a single raise once, from the commit that makes it', () => {
        const errors = run(budget(17), [{ sha: 'aaaaaaa1', text: budget(18) }]);
        expect(errors).toEqual([expect.stringContaining('aaaaaaa: ci/perf-budget.json raises')]);
    });
});

describe('checkCommit', () => {
    const commit = (over) => {
        const merged = { sha: 'abcdef0123', subject: 'fix: x', body: '', files: ['src/Foo.cs'], ...over };
        return { added: Object.fromEntries(merged.files.map((f) => [f, 1])), ...merged };
    };

    it('accepts a Conventional Commit', () => {
        expect(checkCommit(commit({ subject: 'chore(release): 2.10.0 [skip ci]' }))).toEqual({ errors: [], warnings: [] });
    });

    it("accepts git revert's default subject", () => {
        expect(checkCommit(commit({ subject: 'Revert "test: drop the seed"' })).errors).toEqual([]);
    });

    it('rejects an unknown type and a co-author trailer', () => {
        const { errors } = checkCommit(commit({ subject: 'update stuff', body: 'Co-Authored-By: X <x@y>' }));
        expect(errors).toHaveLength(2);
    });

    it('requires a test or an Untestable trailer on any fix, web or server', () => {
        const web = ['src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js'];
        const server = ['src/Jellyfin.Plugin.JuxHomepage/Widgets/Personalized/ScoringService.cs'];
        expect(checkCommit(commit({ files: web })).errors).toHaveLength(1);
        expect(checkCommit(commit({ files: server })).errors).toHaveLength(1);
        expect(checkCommit(commit({ files: web, body: 'Untestable: needs a live bundle' })).errors).toEqual([]);
        expect(checkCommit(commit({ files: [...web, 'test/jux-ui.test.js'] })).errors).toEqual([]);
        expect(checkCommit(commit({ files: [...server, 'src/Jellyfin.Plugin.JuxHomepage.Tests/Personalized/ScoringServiceTests.cs'] })).errors).toEqual([]);
    });

    it('takes as a test only added lines in a test file, not a fixture or a deletion (A-03.c)', () => {
        const web = 'src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js';
        const added = (entries) => ({ [web]: 4, ...entries });
        expect(checkCommit(commit({ files: [web, 'e2e/fixtures/config-2.13.0.xml'], added: added({ 'e2e/fixtures/config-2.13.0.xml': 3 }) })).errors).toHaveLength(1);
        expect(checkCommit(commit({ files: [web, 'test/jux-ui.test.js'], added: added({ 'test/jux-ui.test.js': 0 }) })).errors).toHaveLength(1);
        expect(checkCommit(commit({ files: [web, 'e2e/home.spec.mjs'], added: added({ 'e2e/home.spec.mjs': 2 }) })).errors).toEqual([]);
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

    it('fails on a new family, an extra copy, or a drop the baseline does not record (A-03.b)', () => {
        const baseline = [{ files: ['a.js', 'b.js', 'c.js'], copies: 3 }, { files: ['x.js', 'y.js', 'z.js'], copies: 3 }];
        const kept = baseline[1];
        expect(compareWithBaseline([{ files: ['a.js', 'b.js', 'c.js'], copies: 4 }, kept], baseline).errors).toHaveLength(1);
        expect(compareWithBaseline([...baseline, { files: ['d.js', 'e.js', 'f.js'], copies: 3 }], baseline).errors).toHaveLength(1);
        const shrunk = compareWithBaseline([{ files: ['a.js', 'b.js', 'c.js'], copies: 3 }], baseline);
        expect(shrunk).toEqual({ errors: [expect.stringContaining('x.js')], warnings: [] });
    });
});

describe('duplication pairs and totals', () => {
    const clone = (format, a, b, lines, fragment = 'var x = compute(a, b);') => ({
        format,
        fragment,
        firstFile: { name: a, start: 1, end: lines },
        secondFile: { name: b, start: 1, end: lines }
    });

    it('leaves out clones of using or import lines only', () => {
        expect(isImportOnly('using System;\nusing System.Linq;\n\n{')).toBe(true);
        expect(isImportOnly("import { a } from './a.js';\nconst b = a();")).toBe(false);
    });

    it('counts clones of 15+ lines per pair of files and duplicated lines per format', () => {
        const measured = measureDuplication([
            clone('csharp', 'B.cs', 'A.cs', 20),
            clone('csharp', 'A.cs', 'B.cs', 15),
            clone('csharp', 'A.cs', 'C.cs', 14),
            clone('csharp', 'A.cs', 'D.cs', 30, 'using System;\nusing System.IO;'),
            clone('javascript', 'a.js', 'b.js', 6)
        ]);
        expect(measured).toEqual({ pairs: [{ files: ['A.cs', 'B.cs'], count: 2 }], totals: { csharp: 49, javascript: 6 } });
    });

    it('fails on a new large pair, more duplicated lines, or a drop the baseline does not record (A-03.b)', () => {
        const baseline = { pairs: [{ files: ['A.cs', 'B.cs'], count: 2 }], totals: { csharp: 49, javascript: 6 } };
        const grown = compareDuplication({ pairs: [...baseline.pairs, { files: ['A.cs', 'E.cs'], count: 1 }], totals: { csharp: 70, javascript: 6 } }, baseline);
        expect(grown.errors).toEqual([expect.stringContaining('A.cs | E.cs'), expect.stringContaining('70 duplicated csharp lines')]);
        const shrunk = compareDuplication({ pairs: [{ files: ['A.cs', 'B.cs'], count: 1 }], totals: { csharp: 30, javascript: 6 } }, baseline);
        expect(shrunk.errors).toEqual([expect.stringContaining('A.cs | B.cs'), expect.stringContaining('csharp lines went down to 30')]);
        expect(shrunk.warnings).toEqual([]);
    });
});
