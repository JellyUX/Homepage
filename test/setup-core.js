// Every client script reads window.JuxCore and the tab scripts window.JuxUI; in the browser both
// are injected before them (TransformationPatches.ClientAssets), here they are loaded before each
// test file.
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-core.js';
import '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js';

// The browser receives window.JuxI18n from jux-i18n.js: the "client.*" keys of every
// Localization/<lang>.json (TODO_V4.md 9.4, F-64). The tests build it from the same files.
const localizationDir = resolve(__dirname, '../src/Jellyfin.Plugin.JuxHomepage/Localization');
window.JuxI18n = Object.fromEntries(readdirSync(localizationDir)
    .filter((file) => file.endsWith('.json'))
    .map((file) => [
        file.slice(0, -'.json'.length),
        Object.fromEntries(Object.entries(JSON.parse(readFileSync(resolve(localizationDir, file), 'utf8')))
            .filter(([key]) => key.startsWith('client.')))
    ]));
