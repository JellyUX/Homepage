// The checks of the CI static job and Vitest, run locally before a push (CONTRIBUTING.md "Change Checklist"):
// the static checks over the commits not yet on origin/main, the asset budget, the dead code checks and
// Vitest. Stops at the first failing step and exits non-zero with its name. Run from the repository root:
//   npm run preflight                      commits origin/main..HEAD
//   npm run preflight -- --range A..B      another range, to replay a past push

import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * @param {string} range git revision range whose commits the static checks read
 * @returns {{name: string, args: string[]}[]} node arguments of each step, in order
 */
export function preflightSteps(range) {
    return [
        { name: 'static checks', args: ['ci/static-checks.mjs', '--commits', range] },
        { name: 'asset budget', args: ['ci/asset-budget.mjs'] },
        { name: 'dead code', args: ['ci/dead-code.mjs'] },
        { name: 'Vitest', args: ['node_modules/vitest/vitest.mjs', 'run'] }
    ];
}

/**
 * @param {{steps: {name: string, args: string[]}[], run: (args: string[]) => number, log: (line: string) => void}} input
 *   run starts node with the arguments and returns its exit code.
 * @returns {string | null} the name of the first failing step, or null when every step passed
 */
export function runPreflight({ steps, run, log }) {
    for (const step of steps) {
        log(`== Preflight: ${step.name}`);
        if (run(step.args) !== 0) {
            log(`Preflight failed: ${step.name}.`);
            return step.name;
        }
    }
    log(`Preflight passed: ${steps.map((step) => step.name).join(', ')}.`);
    return null;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const rangeArg = process.argv.indexOf('--range');
    const range = rangeArg > 0 ? process.argv[rangeArg + 1] : 'origin/main..HEAD';
    if (!range?.includes('..')) {
        console.error('Usage: npm run preflight [-- --range A..B]');
        process.exitCode = 2;
    } else {
        const failed = runPreflight({
            steps: preflightSteps(range),
            run: (args) => spawnSync(process.execPath, args, { stdio: 'inherit' }).status ?? 1,
            log: (line) => console.log(line)
        });
        process.exitCode = failed ? 1 : 0;
    }
}
