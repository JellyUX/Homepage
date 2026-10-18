// Authentication with a DeviceId unique to this run: Jellyfin revokes the previous token of a
// DeviceId on each AuthenticateByName, so sharing perf-bench's DeviceId with parallel agents yields 401s.
// Credentials are read from identifiants.txt at run time and never written.
import { readCredentials, serverUrl } from './perf-bench-common.mjs';
export async function authUnique(which = 'user') {
    const dev = `jux-perf-server-${process.pid}-${Date.now()}`;
    const base = `MediaBrowser Client="jux-perf-server", Device="perf-server", DeviceId="${dev}", Version="1.0.0"`;
    const c = readCredentials()[which];
    const res = await fetch(`${serverUrl}/Users/AuthenticateByName`, { method: 'POST', headers: { Authorization: base, 'Content-Type': 'application/json' }, body: JSON.stringify({ Username: c.name, Pw: c.password }) });
    if (!res.ok) throw new Error(`auth failed ${res.status}`);
    const j = await res.json();
    const header = `${base}, Token="${j.AccessToken}"`;
    return { userId: j.User.Id, header, logout: () => fetch(`${serverUrl}/Sessions/Logout`, { method: 'POST', headers: { Authorization: header } }) };
}
