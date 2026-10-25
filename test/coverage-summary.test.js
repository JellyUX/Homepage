import { describe, it, expect } from 'vitest';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import {
    addedLines,
    coberturaFiles,
    coberturaLineHits,
    lcovLineHits,
    changedLinesCoverage,
    coverageRows
} from '../ci/coverage-summary.mjs';

const cobertura = `<coverage><packages><package><classes>
<class name="A" filename="/home/runner/work/Homepage/Homepage/src/Jellyfin.Plugin.JuxHomepage/A.cs"><lines>
<line number="10" hits="3" /><line number="11" hits="0" /></lines></class>
<class name="A+Nested" filename="/home/runner/work/Homepage/Homepage/src/Jellyfin.Plugin.JuxHomepage/A.cs"><lines>
<line number="11" hits="2" /><line number="12" hits="0" /></lines></class>
</classes></package></packages></coverage>`;

describe('addedLines', () => {
    it('reads the new-side line ranges of a zero-context diff', () => {
        const diff = [
            'diff --git a/src/A.cs b/src/A.cs',
            '--- a/src/A.cs',
            '+++ b/src/A.cs',
            '@@ -3 +3 @@ x',
            '@@ -8,0 +9,3 @@ y',
            '@@ -20,2 +22,0 @@ z',
            'diff --git a/src/Gone.js b/src/Gone.js',
            '--- a/src/Gone.js',
            '+++ /dev/null',
            '@@ -1,4 +0,0 @@'
        ].join('\n');

        expect(addedLines(diff)).toEqual(new Map([['src/A.cs', new Set([3, 9, 10, 11])]]));
    });
});

describe('coverage reports', () => {
    it('merges the classes of one Cobertura file, a line counting once', () => {
        expect(coberturaLineHits(cobertura).get('/home/runner/work/Homepage/Homepage/src/Jellyfin.Plugin.JuxHomepage/A.cs'))
            .toEqual(new Map([[10, 3], [11, 2], [12, 0]]));
        expect([...coberturaFiles(cobertura).values()]).toEqual([{ covered: 2, total: 3 }]);
    });

    it('reads the line hits of an lcov report', () => {
        const lcov = 'TN:\nSF:C:\\repo\\src\\Jellyfin.Plugin.JuxHomepage\\Web\\jux-ui.js\nDA:4,1\nDA:5,0\nend_of_record\n';
        expect(lcovLineHits(lcov)).toEqual(new Map([['C:/repo/src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js', new Map([[4, 1], [5, 0]])]]));
    });
});

describe('changedLinesCoverage', () => {
    it('counts only the instrumented added lines, matching paths at a segment boundary', () => {
        const added = new Map([
            ['src/Jellyfin.Plugin.JuxHomepage/A.cs', new Set([10, 11, 12, 13])],
            ['src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js', new Set([4, 5])],
            ['src/Jellyfin.Plugin.JuxHomepage/Unmeasured.cs', new Set([1])]
        ]);
        const js = new Map([['C:/repo/src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js', new Map([[4, 1], [5, 0]])]]);

        const result = changedLinesCoverage(added, [coberturaLineHits(cobertura), js]);

        expect(result).toEqual({
            covered: 3,
            total: 5,
            files: [
                { file: 'src/Jellyfin.Plugin.JuxHomepage/A.cs', covered: 2, total: 3 },
                { file: 'src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js', covered: 1, total: 2 }
            ],
            unreported: ['src/Jellyfin.Plugin.JuxHomepage/Unmeasured.cs']
        });
    });

    it('does not match a file whose name only ends like the changed one', () => {
        const added = new Map([['src/Jellyfin.Plugin.JuxHomepage/A.cs', new Set([1])]]);
        const other = new Map([['/repo/src/Jellyfin.Plugin.JuxHomepage/BA.cs', new Map([[1, 0]])]]);

        expect(changedLinesCoverage(added, [other])).toMatchObject({ total: 0, unreported: ['src/Jellyfin.Plugin.JuxHomepage/A.cs'] });
    });
});

describe('risky files (PROC-05.b)', () => {
    const js = {
        total: { lines: { covered: 1, total: 2 } },
        '/repo/src/Jellyfin.Plugin.JuxHomepage/Web/jux-featured.js': { lines: { covered: 1, total: 2 } }
    };

    it('lists the V6 risk areas, and the template v8 cannot instrument as such', () => {
        const rows = coverageRows(coberturaFiles(cobertura), js, 'c.xml').join('\n');

        for (const area of ['C# Widgets/Service/', 'JS jux-featured.js', 'JS config.js']) {
            expect(rows).toContain(`| ${area} |`);
        }
        expect(rows).toContain('| JS jux-featured.js | 1 | 2 | 50.0 % |');
        expect(rows).toContain('| JS jux-loadsections-inject.js | - | - | not instrumented (template) |');
    });

    it('prints the JS part and says why the C# part is missing when there is no Cobertura report (PROC-05.c)', () => {
        const dir = mkdtempSync(join(tmpdir(), 'jux-coverage-'));
        writeFileSync(join(dir, 'coverage-summary.json'), JSON.stringify(js));
        const env = { ...process.env, GITHUB_STEP_SUMMARY: '' };

        const out = execFileSync(process.execPath, ['ci/coverage-summary.mjs', join(dir, 'missing.xml'), join(dir, 'coverage-summary.json')], { encoding: 'utf8', env });

        expect(out).toContain('no Cobertura report');
        expect(out).toContain('| **JS total** | 1 | 2 | **50.0 %** |');
        expect(out).toContain('| JS jux-featured.js | 1 | 2 | 50.0 % |');
    });
});
