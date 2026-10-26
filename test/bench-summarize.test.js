import { describe, it, expect } from 'vitest';
import { stats, tail } from '../bench/perf-bench-common.mjs';
import { checkWitness } from '../bench/perf-bench-summarize.mjs';

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

describe('native witness of a series (PROC-02.g)', () => {
    const native = { counts: { MovieCount: 1116 }, server: { limits: { nanoCpus: 1e9 } }, plugins: {}, homepage: null };
    const hp = { counts: { MovieCount: 1116 }, server: { limits: { nanoCpus: 1e9 } }, plugins: { 'JellyUX Homepage': '2.17.1.0 Active' }, homepage: { version: '2.17.1.0', sessionTtlMinutes: 15 } };

    it('summarizes a native series on its own', () => {
        expect(checkWitness(native, null)).toEqual([]);
    });

    it('refuses a Homepage series without a native witness', () => {
        expect(checkWitness(hp, null)[0]).toMatch(/no native witness/);
    });

    it('refuses a witness that runs Homepage', () => {
        expect(checkWitness(hp, hp)[0]).toMatch(/not a native witness/);
    });

    it('refuses a witness measured on another data set or profile', () => {
        expect(checkWitness(hp, { ...native, server: { limits: { nanoCpus: 24e9 } } })).toEqual(['witness differs: server.limits.nanoCpus: 1000000000 | 24000000000']);
    });

    it('refuses a witness running other plugins (KefinTweaks, the production stack)', () => {
        expect(checkWitness(hp, { ...native, plugins: { 'JavaScript Injector': '2.0 Active' } })[0]).toMatch(/witness differs: plugins.JavaScript Injector/);
    });

    it('accepts a native witness on the same set and profile', () => {
        expect(checkWitness(hp, native)).toEqual([]);
    });
});
