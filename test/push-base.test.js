import { describe, it, expect } from 'vitest';
import { resolvePushBase } from '../ci/push-base.mjs';

// A fake git: commits that exist, and the merge base of HEAD with origin/main.
function fakeGit({ commits = [], mergeBase = null, head = 'h' }) {
    return (args) => {
        if (args[0] === 'cat-file') {
            return commits.includes(args[2].replace('^{commit}', '')) ? '' : null;
        }
        if (args[0] === 'merge-base') {
            return mergeBase;
        }
        if (args[0] === 'rev-parse') {
            return head;
        }
        return null;
    };
}

describe('resolvePushBase', () => {
    it('takes the "before" of a push when that commit exists', () => {
        expect(resolvePushBase({ before: 'b', head: 'h', git: fakeGit({ commits: ['b'], mergeBase: 'm' }) })).toBe('b');
    });

    it('falls back to the merge base with origin/main for a manual run or a new branch', () => {
        expect(resolvePushBase({ before: '', head: 'h', git: fakeGit({ mergeBase: 'm' }) })).toBe('m');
        expect(resolvePushBase({ before: '0000000000000000000000000000000000000000', head: 'h', git: fakeGit({ mergeBase: 'm' }) })).toBe('m');
        expect(resolvePushBase({ before: 'gone', head: 'h', git: fakeGit({ mergeBase: 'm' }) })).toBe('m');
    });

    it('checks the last commit alone when HEAD is origin/main itself or has no merge base', () => {
        expect(resolvePushBase({ before: '', head: 'h', git: fakeGit({ mergeBase: 'h', head: 'h' }) })).toBe('h~1');
        expect(resolvePushBase({ before: '', head: 'h', git: fakeGit({ mergeBase: null }) })).toBe('h~1');
    });
});
