import { describe, it, expect, vi, afterEach } from 'vitest';
import { timedChain } from '../bench/perf-bench-browser.mjs';

const user = '/Users/3d5344e888ab4e9bbc60972491e9f19f';
const entries = (list) => vi.spyOn(performance, 'getEntriesByType').mockReturnValue(list.map(([name, startTime, responseEnd]) => ({ name: `http://localhost:8112${name}`, startTime, responseEnd })));

describe('timed chain of a home load (PS 1.4 point 2)', () => {
    afterEach(() => { vi.restoreAllMocks(); });

    it('estimates the loadSections call by the first request it sends after its chunk', () => {
        entries([[user, 400, 433], ['/web/home.ab12.chunk.js', 450, 453], ['/web/56213.cd34.chunk.js', 457, 460], ['/UserViews?userId=x', 460, 487]]);
        const c = timedChain({ firstSection: 560, chunk: '/56213\\.[0-9a-f]+\\.chunk\\.js' });
        expect(c.loadSectionsCallEstMs).toBe(460);
        expect(c.loadSectionsCallFromChunk).toBe(0);
    });

    it('falls back to the chunk end when the rows reuse data fetched before the chunk (native, UserViews cached)', () => {
        entries([[user, 400, 433], ['/UserViews?userId=x', 440, 470], ['/web/home.ab12.chunk.js', 450, 453], ['/web/56213.cd34.chunk.js', 457, 893]]);
        const c = timedChain({ firstSection: 1267, chunk: '/56213\\.[0-9a-f]+\\.chunk\\.js' });
        expect(c.loadSectionsCallEstMs).toBe(893);
        expect(c.loadSectionsCallFromChunk).toBe(1);
    });
});
