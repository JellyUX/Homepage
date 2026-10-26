import { describe, it, expect } from 'vitest';
import { compareFingerprints, VARIABLE_FIELDS } from '../bench/bench-fingerprint.mjs';

const base = () => ({
    label: 'perf-bench-cycle',
    capturedAt: '2026-10-26T12:00:00.000Z',
    server: { version: '10.11.11', startedAt: '2026-10-26T11:58:00Z', limits: { nanoCpus: 0, memory: 0 }, storage: { type: 'volume', source: 'jux-bench-hp-1011' } },
    counts: { MovieCount: 1116, SeriesCount: 104, EpisodeCount: 2215 },
    shape: { Movie: { n: 1116, imagesPerItem: 3.94 } },
    user: { playedMovies: 500, likedMovies: 100, inProgressMovies: 10 },
    plugins: { 'File Transformation': '3.0.0.0 Active', 'JellyUX Homepage': '2.17.1.0 Active' },
    homepage: { version: '2.17.1.0', dllMd5: 'aaa', sessionTtlMinutes: 15, featuredSections: 0, deviations: [], overrides: [] },
    warmup: { builtAt: '2026-10-26T11:59:00Z', afterStartMs: 60000 },
    otherContainers: []
});

describe('bench fingerprint comparison (PROC-02.b)', () => {
    it('accepts two series of the same set and configuration, whatever the build, the clock and the warm-up', () => {
        const b = base();
        Object.assign(b, { label: 'perf-bench-endpoints', capturedAt: '2026-10-27T08:00:00.000Z', otherContainers: ['jellyux-test'] });
        b.homepage = { ...b.homepage, version: '2.18.0.10', dllMd5: 'bbb' };
        b.plugins = { ...b.plugins, 'JellyUX Homepage': '2.18.0.10 Active' };
        b.warmup = { builtAt: '2026-10-27T07:59:00Z', afterStartMs: 41000 };
        b.server = { ...b.server, startedAt: '2026-10-27T07:58:00Z' };
        expect(compareFingerprints(base(), b)).toEqual([]);
    });

    it.each([
        ['the data set', (f) => { f.counts.MovieCount = 57; }, 'counts.MovieCount'],
        ['the images per movie', (f) => { f.shape.Movie.imagesPerItem = 0.09; }, 'shape.Movie.imagesPerItem'],
        ['the bench user history', (f) => { f.user.playedMovies = 2; }, 'user.playedMovies'],
        ['the session cache lifetime', (f) => { f.homepage.sessionTtlMinutes = 1; }, 'homepage.sessionTtlMinutes'],
        ['the CPU limit', (f) => { f.server.limits.nanoCpus = 1e9; }, 'server.limits.nanoCpus'],
        ['the storage', (f) => { f.server.storage.type = 'bind'; }, 'server.storage.type'],
        ['another plugin', (f) => { f.plugins['KefinTweaks'] = '0.5.3 Active'; }, 'plugins.KefinTweaks'],
        ['a deviation from the defaults', (f) => { f.homepage.deviations = ['1 featured sections (product 0)']; }, 'homepage.deviations']
    ])('refuses two series that differ by %s', (_, change, key) => {
        const b = base();
        change(b);
        const diff = compareFingerprints(base(), b);
        expect(diff).toHaveLength(1);
        expect(diff[0].startsWith(`${key}:`)).toBe(true);
    });

    it('declares a variable field for the whole warm-up subtree only', () => {
        expect(VARIABLE_FIELDS).toContain('warmup.');
        expect(compareFingerprints({ warmupX: 1 }, { warmupX: 2 })).toEqual(['warmupX: 1 | 2']);
    });
});
