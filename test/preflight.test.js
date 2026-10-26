import { describe, it, expect } from 'vitest';
import { preflightSteps, runPreflight } from '../ci/preflight.mjs';

describe('preflightSteps', () => {
    it('runs the CI static job checks over the given range, then Vitest', () => {
        expect(preflightSteps('origin/main..HEAD')).toEqual([
            { name: 'static checks', args: ['ci/static-checks.mjs', '--commits', 'origin/main..HEAD'] },
            { name: 'asset budget', args: ['ci/asset-budget.mjs'] },
            { name: 'dead code', args: ['ci/dead-code.mjs'] },
            { name: 'Vitest', args: ['node_modules/vitest/vitest.mjs', 'run'] }
        ]);
    });
});

describe('runPreflight', () => {
    const steps = preflightSteps('a..b');

    it('runs every step in order and reports a pass', () => {
        const ran = [];
        const lines = [];
        const failed = runPreflight({ steps, run: (args) => (ran.push(args[0]), 0), log: (line) => lines.push(line) });
        expect(failed).toBeNull();
        expect(ran).toEqual(['ci/static-checks.mjs', 'ci/asset-budget.mjs', 'ci/dead-code.mjs', 'node_modules/vitest/vitest.mjs']);
        expect(lines.at(-1)).toBe('Preflight passed: static checks, asset budget, dead code, Vitest.');
    });

    it('stops at the first failing step and names it', () => {
        const ran = [];
        const lines = [];
        const run = (args) => (ran.push(args[0]), args[0] === 'ci/asset-budget.mjs' ? 1 : 0);
        expect(runPreflight({ steps, run, log: (line) => lines.push(line) })).toBe('asset budget');
        expect(ran).toEqual(['ci/static-checks.mjs', 'ci/asset-budget.mjs']);
        expect(lines.at(-1)).toBe('Preflight failed: asset budget.');
    });
});
