// Archives a series folder (PROC-07.b): a dated zip outside git and its sha256, printed as the registry proof
// form raw:<path>#sha256=<hex> (relative to the repository root when the zip lies inside it).
//   node bench/bench-archive.mjs <series folder> [--out <folder>]     (default: bench/.work/archives)
// The folder must hold its fingerprint.json: a raw without it cannot be compared later (PROC-02.a).
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { repoRoot } from './perf-bench-common.mjs';

/** Registry proof line of an archive: path relative to the repository root when inside it, forward slashes. */
export function proofLine(file, sha256, root = repoRoot) {
    const rel = path.relative(root, file);
    const shown = rel.startsWith('..') || path.isAbsolute(rel) ? file : rel;
    return `raw:${shown.split(path.sep).join('/')}#sha256=${sha256}`;
}

/** Archive name: <series folder name>-<UTC date and time>.zip. */
export function archiveName(dir, now = new Date()) {
    return `${path.basename(path.resolve(dir))}-${now.toISOString().slice(0, 19).replace(/[-:]/g, '').replace('T', '-')}.zip`;
}

export function archiveSeries(dir, outDir) {
    const src = path.resolve(dir);
    if (!existsSync(path.join(src, 'fingerprint.json'))) { throw new Error(`${src} has no fingerprint.json: not a series folder`); }
    mkdirSync(outDir, { recursive: true });
    const zip = path.join(path.resolve(outDir), archiveName(src));
    // bsdtar writes a zip from the extension (-a): Windows ships it as tar.exe, elsewhere zip(1) does the same.
    if (process.platform === 'win32') {
        execFileSync(path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe'), ['-a', '-cf', zip, '-C', path.dirname(src), path.basename(src)]);
    } else {
        execFileSync('zip', ['-qr', zip, path.basename(src)], { cwd: path.dirname(src) });
    }
    const sha256 = createHash('sha256').update(readFileSync(zip)).digest('hex');
    return { zip, bytes: statSync(zip).size, sha256, proof: proofLine(zip, sha256) };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const dir = process.argv[2];
    const i = process.argv.indexOf('--out');
    const out = i > 0 ? process.argv[i + 1] : path.join(repoRoot, 'bench', '.work', 'archives');
    if (!dir) { console.error('usage: node bench/bench-archive.mjs <series folder> [--out <folder>]'); process.exit(2); }
    const r = archiveSeries(dir, out);
    console.log(JSON.stringify(r, null, 2));
}
