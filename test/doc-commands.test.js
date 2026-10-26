import { readFileSync } from 'node:fs';
import { describe, it, expect } from 'vitest';

const scripts = Object.keys(JSON.parse(readFileSync('package.json', 'utf8')).scripts);
const cited = (document) => [...readFileSync(document, 'utf8').matchAll(/\bnpm run ([\w:-]+)/g)].map((m) => m[1]);

describe('npm scripts cited by the docs', () => {
    it.each(['CONTRIBUTING.md', 'README.md'])('every npm run command of %s is a package.json script', (document) => {
        expect(cited(document).filter((name) => !scripts.includes(name))).toEqual([]);
    });

    it('the Change Checklist requires the preflight before a push', () => {
        const contributing = readFileSync('CONTRIBUTING.md', 'utf8');
        const checklist = contributing.slice(contributing.indexOf('## Change Checklist'), contributing.indexOf('## Definition of Done'));
        expect(checklist).toMatch(/`npm run preflight`/);
    });
});
