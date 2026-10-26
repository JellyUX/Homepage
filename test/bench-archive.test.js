import { describe, it, expect } from 'vitest';
import path from 'node:path';
import { proofLine, archiveName } from '../bench/bench-archive.mjs';

describe('series archive (PROC-07.b)', () => {
    it('names the archive after the series folder and the UTC time', () => {
        expect(archiveName('bench/.work/raw/hp-std', new Date('2026-10-26T13:05:09Z'))).toBe('hp-std-20261026-130509.zip');
    });

    it('prints the registry proof form, relative to the repository when the zip lies inside it', () => {
        const root = path.resolve('/repo');
        expect(proofLine(path.join(root, 'audit-v5', 'raw-v6', 'a.zip'), 'ab12', root)).toBe('raw:audit-v5/raw-v6/a.zip#sha256=ab12');
        expect(proofLine(path.resolve('/elsewhere/a.zip'), 'ab12', root)).toBe(`raw:${path.resolve('/elsewhere/a.zip').split(path.sep).join('/')}#sha256=ab12`);
    });
});
