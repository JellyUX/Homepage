// The commit a CI run checks from: the push's "before", or for a manual run or a new branch (no usable
// "before") the merge base with origin/main, or HEAD~1 when HEAD is origin/main itself (TODO_V5
// Clôture.c, A-03). Prints it; reads BEFORE and HEAD_SHA. Run from the repository root:
//   BEFORE=<sha> HEAD_SHA=<sha> node ci/push-base.mjs

import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * @param {{before: string, head: string, git: (args: string[]) => string | null}} input
 *   git runs a git command and returns its trimmed output, or null when it fails.
 * @returns {string}
 */
export function resolvePushBase({ before, head, git }) {
    if (before && !/^0+$/.test(before) && git(['cat-file', '-e', `${before}^{commit}`]) !== null) {
        return before;
    }
    const mergeBase = git(['merge-base', 'origin/main', head]);
    if (mergeBase && mergeBase !== git(['rev-parse', head])) {
        return mergeBase;
    }
    return `${head}~1`;
}

function runGit(args) {
    try {
        return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    } catch {
        return null;
    }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const head = process.env.HEAD_SHA || 'HEAD';
    console.log(resolvePushBase({ before: process.env.BEFORE ?? '', head, git: runGit }));
}
