// Common home rows of the bench comparisons (PROC-02.d), from bench/section-manifest.json: which manifest rows a
// series showed with cards, so two series are compared on the same set of rows.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const manifest = JSON.parse(readFileSync(path.join(here, 'section-manifest.json'), 'utf8'));

/**
 * Matches the row labels a series recorded (rows with cards) against the manifest for one target.
 * @param {'homepage'|'native'|'kefintweaks'} target
 * @param {string[]} labels
 * @returns {{present: string[], missing: string[], notApplicable: string[], outside: string[]}} present and
 *   missing: manifest row ids; outside: labels that match no manifest row.
 */
export function matchManifest(target, labels, rows = manifest.rows) {
    const present = []; const missing = []; const notApplicable = [];
    const matched = new Set();
    for (const row of rows) {
        if (row[target] === null || row[target] === undefined) { notApplicable.push(row.id); continue; }
        const re = new RegExp(row[target]);
        const hits = labels.filter((l) => re.test(l));
        hits.forEach((l) => matched.add(l));
        (hits.length > 0 ? present : missing).push(row.id);
    }
    return { present, missing, notApplicable, outside: [...new Set(labels.filter((l) => !matched.has(l)))] };
}

/** Manifest rows shown on one side and not on the other (empty: the two series measured the same rows). */
export function compareRows(a, b) {
    const only = (x, y) => x.present.filter((id) => !y.present.includes(id));
    return { onlyA: only(a, b), onlyB: only(b, a) };
}
