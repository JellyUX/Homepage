import { describe, it, expect } from 'vitest';
import { injectedAssets, compareAssets } from '../ci/asset-budget.mjs';

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

    it('asks to lower a budget the asset went well under', () => {
        expect(compareAssets({ 'a.js': 900 }, { 'a.js': 1000 }).warnings).toEqual([expect.stringContaining('lower the budget')]);
    });
});
