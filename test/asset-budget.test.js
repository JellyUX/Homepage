import { describe, it, expect } from 'vitest';
import { injectedAssets, compareAssets, clientI18nScript, servedAssets, firstRenderAssets, compareFirstRender } from '../ci/asset-budget.mjs';

describe('clientI18nScript', () => {
    const en = JSON.stringify({ 'client.a': 'Don’t "go" <b>', 'admin.x': 'Admin only' });
    const fr = JSON.stringify({ 'client.a': 'Été' });

    it('builds jux-i18n.js as the server does: client keys only, escaped like System.Text.Json (A-15.a)', () => {
        expect(clientI18nScript([['en.json', en], ['fr.json', fr]])).toBe(
            'window.JuxI18n = {"en":{"client.a":"Don\\u2019t \\u0022go\\u0022 \\u003Cb\\u003E"},"fr":{"client.a":"\\u00C9t\\u00E9"}};\n');
    });

    it('does not grow with an admin key', () => {
        const withAdmin = JSON.stringify({ ...JSON.parse(en), 'admin.y': 'Another admin string' });
        expect(clientI18nScript([['en.json', withAdmin], ['fr.json', fr]])).toBe(clientI18nScript([['en.json', en], ['fr.json', fr]]));
    });
});

describe('injectedAssets', () => {
    it('reads the client asset list in declaration order, the generated labels script included', () => {
        const source = `
    private static readonly ClientAsset[] ClientAssets =
    [
        new("jux-homepage.css", IsStylesheet: true, Module: null),
        new(ClientI18nAsset, IsStylesheet: false, Module: null),
        new("jux-core.js", IsStylesheet: false, Module: null),
    ];

    private static readonly string[] Other = [new("not-an-asset.js", 1)];`;

        expect(injectedAssets(source)).toEqual(['jux-homepage.css', 'jux-i18n.js', 'jux-core.js']);
    });
});

describe('compareAssets', () => {
    it('accepts a size within 2 % over its budget and fails beyond', () => {
        expect(compareAssets({ 'a.js': 1020 }, { 'a.js': 1000 })).toEqual({ errors: [], warnings: [] });
        expect(compareAssets({ 'a.js': 1021 }, { 'a.js': 1000 }).errors).toEqual([expect.stringContaining('a.js is 1021 B gzip')]);
    });

    it('fails on an asset without a budget and on a budget without an asset', () => {
        const { errors } = compareAssets({ 'new.js': 10 }, { 'gone.js': 10 });
        expect(errors).toEqual([expect.stringContaining('new.js'), expect.stringContaining('gone.js')]);
    });

    it('fails on a budget the asset went well under until it is lowered (A-05.a)', () => {
        expect(compareAssets({ 'a.js': 900 }, { 'a.js': 1000 })).toEqual({ errors: [expect.stringContaining('lower the budget')], warnings: [] });
    });
});

describe('servedAssets (A-05.a)', () => {
    it('reads a declaration written with its type name', () => {
        const source = `ClientAsset[] ClientAssets =
    [
        new ClientAsset("jux-a.js", IsStylesheet: false, Module: null),
        new("jux-b.css", IsStylesheet: true, Module: null),
    ];`;
        expect(injectedAssets(source)).toEqual(['jux-a.js', 'jux-b.css']);
    });

    it('adds every embedded web file the injected list leaves out: admin page, loadSections fragment', () => {
        const files = ['jux-a.js', 'config.html', 'config.js', 'config.css', 'jux-loadsections-inject.js', 'jux-native-selectors.json'];
        expect(servedAssets(['jux-i18n.js', 'jux-a.js'], files)).toEqual(['jux-i18n.js', 'jux-a.js', 'config.css', 'config.html', 'config.js', 'jux-loadsections-inject.js']);
    });
});

// H-04 (DR-031): the first render (the home bundle and the stylesheets linked in <head>) has a total budget, so a
// script moved into the home bundle shows even when no file grew.
describe('first render total (H-04)', () => {
    const source = `
    private static readonly ClientAsset[] ClientAssets =
    [
        new("jux-homepage.css", IsStylesheet: true, Module: null),
        new("jux-ui.css", IsStylesheet: true, Module: null, IsJuxUi: true),
        new(ClientI18nAsset, IsStylesheet: false, Module: null),
        new("jux-core.js", IsStylesheet: false, Module: null),
        new("jux-ui.js", IsStylesheet: false, Module: null, IsJuxUi: true),
    ];

    private static readonly HashSet<string> HomeBundleScripts = new(StringComparer.Ordinal)
    {
        ClientI18nAsset, "jux-core.js",
    };`;

    it('reads the home bundle scripts and the stylesheets linked at once (not the later jux-ui.css)', () => {
        expect(firstRenderAssets(source)).toEqual(['jux-i18n.js', 'jux-core.js', 'jux-homepage.css']);
    });

    it('refuses a source where the home bundle cannot be read', () => {
        expect(() => firstRenderAssets('class X {}')).toThrow(/HomeBundleScripts/);
    });

    it('sums the first render files against the budget, 2 % either way', () => {
        const measured = { 'a.js': 600, 'b.css': 400, 'later.js': 5000 };
        expect(compareFirstRender(measured, ['a.js', 'b.css'], 1000)).toEqual([]);
        expect(compareFirstRender(measured, ['a.js', 'b.css', 'later.js'], 1000)).toEqual([expect.stringContaining('first render is 6000 B gzip')]);
        expect(compareFirstRender(measured, ['a.js'], 1000)).toEqual([expect.stringContaining('lower the budget')]);
        expect(compareFirstRender(measured, ['a.js'], undefined)).toEqual([expect.stringContaining('no firstRender budget')]);
    });
});
