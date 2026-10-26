import { describe, it, expect } from 'vitest';
import { manifest, matchManifest, compareRows } from '../bench/section-manifest.mjs';
import { targetOf } from '../bench/perf-bench-summarize.mjs';

describe('common section manifest (PROC-02.d)', () => {
    it('gives every row an id and a pattern or null per target', () => {
        for (const row of manifest.rows) {
            expect(row.id).toMatch(/^[a-z-]+$/);
            for (const target of ['homepage', 'native', 'kefintweaks']) {
                expect(row[target] === null || typeof row[target] === 'string').toBe(true);
            }
        }
    });

    it('matches the Homepage defaults and the native rows to the same manifest rows', () => {
        const hp = matchManifest('homepage', ['jux-native-continue-watching', 'jux-native-next-up', 'jux-native-recently-added-movies', 'jux-native-recently-added-episodes', 'jux-native-my-media', 'jux-local-top-rated']);
        const native = matchManifest('native', ['My Media', 'Continue Watching', 'Next Up', 'Recently Added in Films', 'Recently Added in Séries']);
        expect(hp.outside).toEqual(['jux-local-top-rated']);
        expect(native.notApplicable).toEqual(['watchlist']);
        expect(compareRows(hp, native)).toEqual({ onlyA: [], onlyB: [] });
    });

    it('reports a manifest row shown on one side only', () => {
        const a = matchManifest('native', ['Continue Watching', 'Next Up']);
        const b = matchManifest('native', ['Continue Watching']);
        expect(compareRows(a, b)).toEqual({ onlyA: ['next-up'], onlyB: [] });
    });

    it('takes the target of a series from its fingerprint', () => {
        expect(targetOf({ homepage: { version: '2.17.1.0' }, plugins: {} })).toBe('homepage');
        expect(targetOf({ homepage: null, plugins: { 'JavaScript Injector': '1.0 Active' } })).toBe('kefintweaks');
        expect(targetOf({ homepage: null, plugins: { TMDb: '10.11.11.0 Active' } })).toBe('native');
    });
});
