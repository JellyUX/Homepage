import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { stripComments as stripCodeComments } from '../ci/static-checks.mjs';

// Keeps Web/jux-native-selectors.json in step with the scripts: every native class, id, attribute or
// custom element the client scripts read or write must be listed, so the drift test
// (JellyfinWebDriftTests) can check each one against the Jellyfin web bundle (TODO_V4.md 3.3, F-11).
const WEB_DIR = 'src/Jellyfin.Plugin.JuxHomepage/Web';
const INVENTORY = path.join(WEB_DIR, 'jux-native-selectors.json');

// Comments blanked by the static checks' reader, which skips strings and regex literals (A-03.e).
const stripComments = (src) => stripCodeComments(src, '.js');

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

/** Attribute values the selectors match on, as "attribute=value" strings ([data-id="resume"]). */
export function extractAttributeValues(source) {
    const src = stripComments(source);
    const values = new Set();
    for (const m of src.matchAll(/\.(?:querySelector(?:All)?|closest|matches)\(\s*(['"])(.*?)\1/g)) {
        for (const v of m[2].matchAll(/\[([\w-]+)=["']?([\w-]+)["']?\]/g)) {
            if (!/jux/i.test(v[2])) values.add(`${v[1]}=${v[2]}`);
        }
    }
    return values;
}

/** Document-level class reads and writes, as "element:class" strings (document.body / documentElement). */
export function extractCarrierUses(source) {
    const src = stripComments(source);
    const uses = new Set();
    for (const m of src.matchAll(/document\.(body|documentElement)\.classList\.(?:add|remove|contains|toggle)\(\s*(['"])([\w-]+)\2/g)) {
        uses.add(`${m[1]}:${m[3]}`);
    }
    return uses;
}

// JellyUX state classes without the jux prefix, set by the scripts in concatenated strings
// (jux-ui.js: 'jux-progress-chunk ' + (i < watched ? 'watched' : 'unwatched')).
const OWN_CSS_CLASSES = new Set(['watched', 'unwatched']);

/** Native classes a stylesheet's selectors name, as "class:name" strings (jux-* and own state classes excluded). */
export function extractCssNativeClasses(source) {
    const selectors = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\{[^{}]*\}/g, '\n');
    const tokens = new Set();
    for (const m of selectors.matchAll(/\.([A-Za-z][\w-]*)/g)) {
        if (!/jux/i.test(m[1]) && !OWN_CSS_CLASSES.has(m[1])) {
            tokens.add(`class:${m[1]}`);
        }
    }
    return tokens;
}

function scriptTokens() {
    const tokens = new Set();
    for (const file of readdirSync(WEB_DIR).filter((f) => /^jux-.*\.js$/.test(f) && f !== 'jux-loadsections-inject.js')) {
        extractNativeTokens(readFileSync(path.join(WEB_DIR, file), 'utf8')).forEach((t) => tokens.add(t));
    }
    // A stylesheet that hooks on a native class breaks as silently as a script when Jellyfin renames it.
    for (const file of readdirSync(WEB_DIR).filter((f) => /^jux-.*\.css$/.test(f))) {
        extractCssNativeClasses(readFileSync(path.join(WEB_DIR, file), 'utf8')).forEach((t) => tokens.add(t));
    }
    adminPageTokens().forEach((t) => tokens.add(t));
    return tokens;
}

/**
 * Native tokens of the admin page (config.js, config.html, config.css), which styles itself with the
 * dashboard's own classes (annex A "Inventaire natif"). The ids and attributes the page writes itself
 * (id="...", data-i18n, role) are its own, not Jellyfin's.
 */
export function adminPageTokens() {
    const read = (file) => readFileSync(path.join(WEB_DIR, file), 'utf8');
    const markup = read('config.html') + read('config.js');
    const ownIds = new Set([...markup.matchAll(/\bid="([\w-]+)/g)].map((m) => `id:${m[1]}`));
    const ownAttributes = new Set([...markup.matchAll(/\s([a-z][\w-]*)="/g)].map((m) => `attribute:${m[1]}`));
    const tokens = new Set([
        ...extractNativeTokens(read('config.js')),
        ...extractNativeTokens(read('config.html')),
        ...extractCssNativeClasses(read('config.css'))
    ]);
    return new Set([...tokens].filter((t) => !ownIds.has(t) && !ownAttributes.has(t)));
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

describe('extractCssNativeClasses', () => {
    it('reads the native classes of the selectors, not of declarations, comments or JellyUX names', () => {
        const css = `
            /* .commentClass */
            .jux-widget-section.emby-scroller-container > .sectionTitleContainer { padding-right: 8.4em; }
            .jux-progress-chunk.watched { background: url("a.b.png"); }
            @media (max-width: 50em) { .layout-tv .card { opacity: 0.5; } }
        `;
        expect([...extractCssNativeClasses(css)].sort()).toEqual([
            'class:card', 'class:emby-scroller-container', 'class:layout-tv', 'class:sectionTitleContainer'
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

    it('lists every native attribute value the client scripts match on, with the bundle fragment that sets it (TODO_V5 closure criterion b)', () => {
        const inventory = JSON.parse(readFileSync(INVENTORY, 'utf8'));
        const listed = new Set((inventory.attributeValues ?? []).map((v) => `${v.attribute}=${v.value}`));
        const used = new Set();
        for (const file of readdirSync(WEB_DIR).filter((f) => /^jux-.*\.js$/.test(f))) {
            extractAttributeValues(readFileSync(path.join(WEB_DIR, file), 'utf8')).forEach((v) => used.add(v));
        }
        expect([...used].sort()).toEqual([...listed].sort());
        (inventory.attributeValues ?? []).forEach((v) => expect(v.evidence).toContain(`"${v.value}"`));
    });

    it('names the element of every document-level class the scripts read, the splice fragment included (FIAB-05)', () => {
        const inventory = JSON.parse(readFileSync(INVENTORY, 'utf8'));
        const listed = new Set(inventory.carriers.map((c) => `${c.element}:${c.class}`));
        const used = new Set();
        for (const file of readdirSync(WEB_DIR).filter((f) => /^jux-.*\.js$/.test(f))) {
            extractCarrierUses(readFileSync(path.join(WEB_DIR, file), 'utf8')).forEach((u) => used.add(u));
        }
        expect([...used].sort()).toEqual([...listed].sort());
        inventory.carriers.forEach((c) => expect(c.evidence).toContain(`${c.element}.classList`));
    });
});

describe('extractAttributeValues', () => {
    it('reads the attribute values of the selectors, skipping JellyUX values', () => {
        expect([...extractAttributeValues(`
            scroller.querySelector('[data-id="playallfromhere"]');
            el.closest('.card[data-action=play]');
            el.querySelector('[data-jux-kind="jux-row"]');
        `)].sort()).toEqual(['data-action=play', 'data-id=playallfromhere']);
    });
});

describe('extractCarrierUses', () => {
    it('reads the element a document-level class is read on', () => {
        expect([...extractCarrierUses(`
            if (document.body.classList.contains('layout-tv')) {}
            document.documentElement.classList.add("layout-desktop");
            el.classList.contains('card');
        `)].sort()).toEqual(['body:layout-tv', 'documentElement:layout-desktop']);
    });
});
