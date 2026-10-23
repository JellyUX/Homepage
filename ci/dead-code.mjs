// Dead code checks of the plugin (TODO_V5 PROC-06.b), promoted from the audit's quality scripts:
//   - C#: a member or type name seen only at its declarations in the plugin sources is a candidate; each
//     declaring file must be listed in ci/dead-code-allowlist.json ("File.cs:Name") with the reason it has
//     no visible caller (HTTP route, framework entry point, JSON property, test seam); a listed entry that
//     is no longer a candidate is reported so the list stays short;
//   - JS: a function of Web/*.js, declared or assigned to a variable, named nowhere else in the
//     production files;
//   - CSS: a jux class or id of Web/*.css that no script, page or C# file names, nor builds from a prefix;
//   - i18n: a key missing from one language, or a non-client key no production file names, nor builds
//     from a prefix ('admin.warning.' + code).
// JS, CSS and i18n tolerate nothing. Run from the repository root: node ci/dead-code.mjs

import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const PLUGIN = 'src/Jellyfin.Plugin.JuxHomepage/';
const WEB = `${PLUGIN}Web/`;
const LOCALIZATION = `${PLUGIN}Localization/`;
const ALLOWLIST = 'ci/dead-code-allowlist.json';

const CS_DECLARATION = /^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s+(?:(?:static|readonly|override|virtual|abstract|sealed|async|const|new|partial|required)\s+)*[\w<>[\],.?() ]+?\s+(\w+)\s*(?:\(|\{|=|;)/gm;
const NOT_A_NAME = new Set(['get', 'set', 'init', 'class', 'record', 'interface', 'enum', 'struct']);

const escape = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const wordCount = (name, text) => (text.match(new RegExp(`\\b${escape(name)}\\b`, 'g')) ?? []).length;

/**
 * Whether a name ending in a variable part is built from one of its literal prefixes, as in
 * 'jux-state-' + kind or 'admin.warning.' + code.
 * @param {string} name
 * @param {string} separator
 * @param {string} text
 * @returns {boolean}
 */
export function builtFromPrefix(name, separator, text) {
    const parts = name.split(separator);
    for (let i = parts.length - 1; i >= 2; i--) {
        const prefix = parts.slice(0, i).join(separator) + separator;
        if ([`'${prefix}'`, `"${prefix}"`, `${prefix}' +`, `${prefix}" +`].some((s) => text.includes(s))) {
            return true;
        }
    }
    return false;
}

/**
 * C# names seen in the plugin sources only at their declarations (an interface and its implementation
 * count as one declaration each).
 * @param {[string, string][]} sources Plugin .cs, .js, .html and .json files, as [path, text].
 * @returns {Map<string, string[]>} Candidate name -> declaring files.
 */
export function csUnreferenced(sources) {
    // Declarations are counted one by one, so two unused overloads in one file are still candidates.
    const declared = new Map();
    for (const [file, text] of sources.filter(([f]) => f.endsWith('.cs'))) {
        for (const match of text.matchAll(CS_DECLARATION)) {
            if (!NOT_A_NAME.has(match[1])) {
                const entry = declared.get(match[1]) ?? { files: new Set(), count: 0 };
                entry.files.add(file);
                entry.count++;
                declared.set(match[1], entry);
            }
        }
    }
    const corpus = sources.map(([, text]) => text).join('\n');
    return new Map([...declared]
        .filter(([name, entry]) => wordCount(name, corpus) <= entry.count)
        .map(([name, entry]) => [name, [...entry.files].sort()]));
}

/**
 * Functions of the web scripts that no production file names apart from their declaration and their
 * own export entry ("name: name").
 * @param {[string, string][]} scripts Web/*.js files, as [path, code].
 * @param {string} production Every production file of the plugin, joined.
 * @param {(code: string, script: boolean) => object} parse A JavaScript parser returning a Babel AST.
 * @returns {{file: string, line: number, name: string}[]}
 */
export function jsUnused(scripts, production, parse) {
    const unused = [];
    for (const [file, code] of scripts) {
        const names = [];
        (function walk(node) {
            if (!node || typeof node.type !== 'string') {
                return;
            }
            if (node.type === 'FunctionDeclaration' && node.id) {
                names.push([node.id.name, node.loc.start.line]);
            }
            // const f = () => {} and var f = function () {} (A-06.a).
            if (node.type === 'VariableDeclarator' && node.id?.type === 'Identifier' && /Function/.test(node.init?.type ?? '')) {
                names.push([node.id.name, node.loc.start.line]);
            }
            if ((node.type === 'ObjectProperty' && /Function/.test(node.value?.type ?? '')) || node.type === 'ObjectMethod') {
                if (node.key?.name) {
                    names.push([node.key.name, node.loc.start.line]);
                }
            }
            for (const [key, value] of Object.entries(node)) {
                if (key !== 'loc') {
                    (Array.isArray(value) ? value : [value]).forEach(walk);
                }
            }
        })(parse(code, !file.endsWith('config.js')).program);
        for (const [name, line] of names) {
            const exportEntries = (code.match(new RegExp(`\\b${escape(name)}\\s*:\\s*${escape(name)}\\b`, 'g')) ?? []).length;
            if (wordCount(name, production) - 1 - exportEntries * 2 <= 0) {
                unused.push({ file, line, name });
            }
        }
    }
    return unused;
}

/**
 * JellyUX classes and ids of the stylesheets that no script, page or C# file names or builds.
 * @param {[string, string][]} stylesheets Web/*.css files, as [path, text].
 * @param {string} users Scripts, pages and C# files of the plugin, joined.
 * @returns {{file: string, name: string}[]}
 */
export function cssUnreferenced(stylesheets, users) {
    const unreferenced = [];
    for (const [file, text] of stylesheets) {
        const selectors = [...text.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{/g)].map((m) => m[1]);
        const names = new Set(selectors.flatMap((s) => [...s.matchAll(/[.#]([A-Za-z_][\w-]*)/g)].map((m) => m[1])).filter((n) => /jux/i.test(n)));
        for (const name of names) {
            const named = new RegExp(`(^|[^\\w-])${escape(name)}($|[^\\w-])`).test(users);
            if (!named && !builtFromPrefix(name, '-', users)) {
                unreferenced.push({ file, name });
            }
        }
    }
    return unreferenced;
}

/**
 * Keys missing from one of the two languages, and non-client keys no production file names or builds.
 * Client keys (client.*) reach the scripts through the generated jux-i18n.js and are not checked here.
 * @param {Record<string, string>} en
 * @param {Record<string, string>} fr
 * @param {string} production Every production file of the plugin outside Localization/, joined.
 * @returns {string[]} Problems, one sentence each.
 */
export function i18nProblems(en, fr, production) {
    const problems = [
        ...Object.keys(en).filter((k) => !(k in fr)).map((k) => `${k} is in en.json, not in fr.json.`),
        ...Object.keys(fr).filter((k) => !(k in en)).map((k) => `${k} is in fr.json, not in en.json.`)
    ];
    for (const key of Object.keys(en).filter((k) => !k.startsWith('client.'))) {
        if (!production.includes(key) && !builtFromPrefix(key, '.', production)) {
            problems.push(`${key} is named by no production file.`);
        }
    }
    return problems;
}

/**
 * Compares the C# candidates with the allowlist, one entry per declaring file and name, so a new dead
 * declaration of a listed name in another file is still reported (A-06.a).
 * @param {Map<string, string[]>} candidates Name -> declaring files, relative to the plugin folder.
 * @param {Record<string, string>} allowlist "File.cs:Name" -> reason it has no visible caller.
 * @returns {{errors: string[], warnings: string[]}}
 */
export function checkAllowlist(candidates, allowlist) {
    const found = new Set([...candidates].flatMap(([name, files]) => files.map((file) => `${file}:${name}`)));
    return {
        errors: [...candidates].flatMap(([name, files]) => {
            const unlisted = files.filter((file) => !allowlist[`${file}:${name}`]);
            return unlisted.length === 0
                ? []
                : [`${name} (${unlisted.join(', ')}) has no caller: delete it, or list it with its reason in ${ALLOWLIST}.`];
        }),
        warnings: Object.keys(allowlist)
            .filter((key) => !found.has(key))
            .map((key) => `${key} is listed in ${ALLOWLIST} but has callers now: remove the entry.`)
    };
}

function main() {
    const require = createRequire(import.meta.url);
    const { parse } = require('@babel/parser');
    const tracked = execFileSync('git', ['ls-files', PLUGIN], { encoding: 'utf8' }).split('\n').filter(Boolean);
    const read = (files) => files.map((f) => [f, readFileSync(f, 'utf8')]);
    const sources = read(tracked.filter((f) => /\.(cs|js|html|json)$/.test(f)));
    const scripts = sources.filter(([f]) => f.startsWith(WEB) && f.endsWith('.js') && !f.endsWith('jux-loadsections-inject.js'));
    const production = sources.filter(([f]) => /\.(cs|js|html)$/.test(f)).map(([, t]) => t).join('\n');
    const productionOutsideLocalization = sources.filter(([f]) => !f.startsWith(LOCALIZATION) && /\.(cs|js|html)$/.test(f)).map(([, t]) => t).join('\n');

    const allowlist = JSON.parse(readFileSync(ALLOWLIST, 'utf8')).csharp;
    const relative = new Map([...csUnreferenced(sources)].map(([name, files]) => [name, files.map((f) => f.slice(PLUGIN.length))]));
    const cs = checkAllowlist(relative, allowlist);
    const js = jsUnused(scripts, production, (code, script) => parse(code, { sourceType: script ? 'script' : 'module', errorRecovery: true }))
        .map((u) => `${u.file}:${u.line} ${u.name} has no caller in the production files: delete it.`);
    const css = cssUnreferenced(read(tracked.filter((f) => f.startsWith(WEB) && f.endsWith('.css'))), production)
        .map((u) => `${u.file}: .${u.name} is named by no script, page or C# file: delete the rule.`);
    const i18n = i18nProblems(
        JSON.parse(readFileSync(`${LOCALIZATION}en.json`, 'utf8')),
        JSON.parse(readFileSync(`${LOCALIZATION}fr.json`, 'utf8')),
        productionOutsideLocalization);

    const errors = [...cs.errors, ...js, ...css, ...i18n];
    cs.warnings.forEach((w) => console.log(`warning: ${w}`));
    errors.forEach((e) => console.error(`error: ${e}`));
    console.log(`Dead code: ${Object.keys(allowlist).length} listed C# declarations, ${errors.length} error(s), ${cs.warnings.length} warning(s).`);
    process.exitCode = errors.length > 0 ? 1 : 0;
}

if (process.argv[1]?.endsWith('dead-code.mjs')) {
    main();
}
