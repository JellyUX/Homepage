// Small API client for the disposable E2E bench on 8098 (`node e2e/setup.mjs up`).
// Reads the bench's generated credentials from e2e/.work/env.json (never printed).
//   node bench/reliability-api.mjs <METHOD> <route> [jsonBody] [--user restricted] [--raw]
// Prints "HTTP <status> <ms> ms <bytes> B" then the body (truncated to 1500 chars unless --raw).
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const env = JSON.parse(readFileSync(path.join(repoRoot, 'e2e', '.work', 'env.json'), 'utf8'));
// One DeviceId per run: Jellyfin revokes the previous token of a DeviceId on each AuthenticateByName.
const AUTH = `MediaBrowser Client="jux-bench", Device="bench", DeviceId="jux-bench-rel-${randomUUID()}", Version="1.0.0"`;

export async function token(who = 'admin') {
    const u = env[who];
    const res = await fetch(`${env.serverUrl}/Users/AuthenticateByName`, {
        method: 'POST',
        headers: { Authorization: AUTH, 'Content-Type': 'application/json' },
        body: JSON.stringify({ Username: u.name, Pw: u.password })
    });
    if (!res.ok) throw new Error(`auth ${who}: HTTP ${res.status}`);
    const j = await res.json();
    return { token: j.AccessToken, userId: j.User.Id };
}

export async function call(method, route, { body, tok, timeoutMs = 120000 } = {}) {
    const headers = { Authorization: `${AUTH}, Token="${tok}"` };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const t0 = performance.now();
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), timeoutMs);
    try {
        const res = await fetch(`${env.serverUrl}${route}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: ctrl.signal });
        const text = await res.text();
        return { status: res.status, ms: Math.round(performance.now() - t0), text };
    } catch (e) {
        return { status: 0, ms: Math.round(performance.now() - t0), text: String(e) };
    } finally {
        clearTimeout(timer);
    }
}

export { env };

if (process.argv[1] && process.argv[1].endsWith("reliability-api.mjs")) {
    const [method, route, bodyArg] = process.argv.slice(2);
    if (method && route) {
        const who = process.argv.includes('--user') ? process.argv[process.argv.indexOf('--user') + 1] : 'admin';
        const { token: tok, userId } = await token(who);
        const r = await call(method, route.replace('{userId}', userId), { body: bodyArg && !bodyArg.startsWith('--') ? JSON.parse(bodyArg) : undefined, tok });
        console.log(`HTTP ${r.status} ${r.ms} ms ${r.text.length} B`);
        console.log(process.argv.includes('--raw') ? r.text : r.text.slice(0, 1500));
    }
}
