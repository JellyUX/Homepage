import { describe, it, expect } from 'vitest';
import { stats, tail } from '../bench/perf-bench-common.mjs';

const range = (n) => Array.from({ length: n }, (_, i) => i + 1);

describe('bench tail notation (V3, PROC-02.c)', () => {
    it('reports no p95 below 20 values, but the maximum with its n', () => {
        const s = stats(range(19));
        expect(s.p95).toBeNull();
        expect(tail(s)).toBe('max 19 (19)');
    });

    it('reports the nearest-rank p95 from 20 values', () => {
        const s = stats(range(20));
        expect(s.p95).toBe(19);
        expect(tail(s)).toBe('19');
    });

    it('reports n/a for an empty series', () => {
        expect(tail(stats([]))).toBe('n/a');
    });
});
