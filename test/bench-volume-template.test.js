import { describe, it, expect } from 'vitest';
import { compareContent, compareCounts } from '../bench/volume-template.mjs';
import { checkMovieImages } from '../bench/db-shape.mjs';

describe('reference volume (REV-05.a)', () => {
    const stamp = { content: { files: 10, bytes: 100, dbSha256: 'x', wal: false } };

    it('proves a copy equal to its template by files, bytes, database hash and WAL', () => {
        expect(compareContent(stamp, { files: 10, bytes: 100, dbSha256: 'x', wal: false })).toEqual([]);
        expect(compareContent(stamp, { files: 10, bytes: 100, dbSha256: 'y', wal: true })).toEqual(['dbSha256: stamp x, now y', 'wal: stamp false, now true']);
    });

    it('refuses a migration that drops items (PROC-02.f)', () => {
        expect(compareCounts({ MovieCount: 3071 }, { MovieCount: 3071 })).toEqual([]);
        expect(compareCounts({ MovieCount: 3071, SeriesCount: 1 }, { MovieCount: 57, SeriesCount: 1 })).toEqual(['MovieCount: 3071 -> 57']);
    });

    it('ignores ItemCount, 0 on 10.11 and the total on 12.3', () => {
        expect(compareCounts({ MovieCount: 1112, ItemCount: 0 }, { MovieCount: 1112, ItemCount: 3639 })).toEqual([]);
    });

    it('holds the reference set to at least 3 images per movie (PROC-01.a)', () => {
        expect(checkMovieImages({ types: { Movie: { imagesPerItem: 3.94 } } }, 3)).toBeNull();
        expect(checkMovieImages({ types: { Movie: { imagesPerItem: 0.09 } } }, 3)).toBe('0.09 images per movie, below 3');
        expect(checkMovieImages({ types: {} }, 3)).toBe('no movie in the database');
    });
});
