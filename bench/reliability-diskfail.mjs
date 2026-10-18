// Reliability check: DiskJsonCache / UserConfigurationStore write failures on the 8098 bench.
// The caller makes the next write fail by creating a DIRECTORY at "<file>.tmp" (WriteAllText then throws),
// which stands in for a full or read-only disk (the container runs as root, so chmod would not block it).
//   node bench/reliability-diskfail.mjs markplayed   marks the first movie played for admin
//   node bench/reliability-diskfail.mjs history      reads MovieHistory/Items 3 times
//   node bench/reliability-diskfail.mjs settings     saves then rereads the user settings
import { token, call } from './reliability-api.mjs';

const { token: tok, userId } = await token('admin');
const mode = process.argv[2];

if (mode === 'markplayed') {
    const items = JSON.parse((await call('GET', `/Items?recursive=true&includeItemTypes=Movie&userId=${userId}`, { tok })).text).Items;
    const r = await call('POST', `/UserPlayedItems/${items[2].Id}?userId=${userId}`, { tok });
    console.log(`marked played '${items[2].Name}' HTTP ${r.status}`);
} else if (mode === 'history') {
    for (let i = 0; i < 3; i++) {
        const r = await call('GET', `/JuxHomepage/MovieHistory/Items?userId=${userId}&startIndex=0&limit=20`, { tok });
        let n = '?';
        try { const j = JSON.parse(r.text); n = (j.Items || []).length + ' items, total ' + j.TotalRecordCount; } catch { /* not JSON */ }
        console.log(`MovieHistory/Items #${i + 1}: HTTP ${r.status} ${r.ms} ms -> ${n}`);
    }
} else if (mode === 'settings') {
    const before = JSON.parse((await call('GET', `/JuxHomepage/UserSettings?userId=${userId}`, { tok })).text);
    const key = (before.Sections || before.HideableSections || [])[0]?.Key ?? (before.Sections || [])[0]?.SectionKey;
    console.log(`settings keys: ${Object.keys(before).join(',')}; first hideable=${key}`);
    const save = await call('POST', `/JuxHomepage/UserSettings?userId=${userId}`, { tok, body: { Enabled: true, HiddenSections: key ? [key] : [] } });
    console.log(`POST UserSettings HTTP ${save.status}`);
    const after = await call('GET', `/JuxHomepage/UserSettings?userId=${userId}`, { tok });
    console.log(`GET after save: ${after.text.slice(0, 400)}`);
}
