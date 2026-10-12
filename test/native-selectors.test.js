import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

// Keeps Web/jux-native-selectors.json in step with the scripts: every native class, id, attribute or
// custom element the client scripts read or write must be listed, so the drift test
// (JellyfinWebDriftTests) can check each one against the Jellyfin web bundle (TODO_V4.md 3.3, F-11).
const WEB_DIR = 'src/Jellyfin.Plugin.JuxHomepage/Web';
const INVENTORY = path.join(WEB_DIR, 'jux-native-selectors.json');

function stripComments(src) {
    return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"\\])\/\/[^\n]*/g, '$1');
}

/** Native tokens used by one script, as "kind:name" strings (JellyUX's own jux-* names excluded). */
export function extractNativeTokens(source) {
    const src = stripComments(source);
    const tokens = new Set();
    const add = (kind, name) => {
        if (name && !/jux/i.test(name)) {
            tokens.add(`${kind}:${name}`);
        }
    };
    const addClasses = (text) => text.split(/\s+/).filter((c) => /^[A-Za-z][\w-]*$/.test(c)).forEach((c) => add('class', c));

    for (const m of src.matchAll(/\.(?:querySelector(?:All)?|closest|matches)\(\s*(['"])(.*?)\1/g)) {
        for (const c of m[2].matchAll(/\.([A-Za-z][\w-]*)/g)) add('class', c[1]);
        for (const c of m[2].matchAll(/#([A-Za-z][\w-]*)/g)) add('id', c[1]);
        for (const c of m[2].matchAll(/\[([\w-]+)/g)) add('attribute', c[1]);
    }
    for (const m of src.matchAll(/getElementById\(\s*(['"])([\w-]+)\1/g)) add('id', m[2]);
    for (const m of src.matchAll(/classList\.(?:add|remove|contains|toggle)\(([^)]*)\)/g)) {
        for (const s of m[1].matchAll(/(['"])([\w-]+)\1/g)) add('class', s[2]);
    }
    for (const m of src.matchAll(/className\s*\+?=\s*((?:(['"])[^'"]*\2\s*\+?\s*)+)/g)) {
        for (const s of m[1].matchAll(/(['"])([^'"]*)\1/g)) addClasses(s[2]);
    }
    // class="..." inside HTML strings; a concatenated part (class="' + x + '") is not a literal name.
    for (const m of src.matchAll(/class="([^"]*)"/g)) {
        m[1].split(/['"]\s*\+[^+]*\+\s*['"]/).forEach(addClasses);
    }
    for (const m of src.matchAll(/\bis="([\w-]+)"|setAttribute\(\s*'is',\s*'([\w-]+)'\)|\{\s*is:\s*'([\w-]+)'/g)) {
        add('element', m[1] || m[2] || m[3]);
    }
    return tokens;
}

function scriptTokens() {
    const tokens = new Set();
    for (const file of readdirSync(WEB_DIR).filter((f) => /^jux-.*\.js$/.test(f) && f !== 'jux-loadsections-inject.js')) {
        extractNativeTokens(readFileSync(path.join(WEB_DIR, file), 'utf8')).forEach((t) => tokens.add(t));
    }
    return tokens;
}

function inventoryTokens() {
    const inventory = JSON.parse(readFileSync(INVENTORY, 'utf8'));
    const kinds = { classes: 'class', ids: 'id', attributes: 'attribute', elements: 'element' };
    return new Set(Object.entries(kinds).flatMap(([key, kind]) => inventory[key].map((name) => `${kind}:${name}`)));
}

describe('extractNativeTokens', () => {
    it('finds classes, ids, attributes and custom elements, skipping JellyUX names', () => {
        const src = `
            el.querySelector('.actionSheetScroller [data-id="resume"]');
            document.getElementById('indexPage');
            el.classList.add('is-active', 'jux-hidden');
            btn.className = 'emby-tab-button emby-button';
            html += '<div class="' + barClass + ' sectionTitle">';
            html += '<button is="paper-icon-button-light">';
        `;
        expect([...extractNativeTokens(src)].sort()).toEqual([
            'attribute:data-id', 'class:actionSheetScroller', 'class:emby-button', 'class:emby-tab-button',
            'class:is-active', 'class:sectionTitle', 'element:paper-icon-button-light', 'id:indexPage'
        ]);
    });
});

describe('Web/jux-native-selectors.json', () => {
    it('lists every native token the client scripts use', () => {
        const listed = inventoryTokens();
        expect([...scriptTokens()].filter((t) => !listed.has(t)).sort()).toEqual([]);
    });

    it('lists no token the client scripts no longer use', () => {
        const used = scriptTokens();
        expect([...inventoryTokens()].filter((t) => !used.has(t)).sort()).toEqual([]);
    });
});
