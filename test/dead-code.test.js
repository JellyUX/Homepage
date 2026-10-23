import { describe, it, expect } from 'vitest';
import { parse } from '@babel/parser';
import { builtFromPrefix, csUnreferenced, jsUnused, cssUnreferenced, i18nProblems, checkAllowlist } from '../ci/dead-code.mjs';

const babel = (code, script) => parse(code, { sourceType: script ? 'script' : 'module', errorRecovery: true });

describe('csUnreferenced', () => {
    it('reports a name seen only at its declarations, an interface and its implementation counting once each', () => {
        const sources = [
            ['IStore.cs', 'public interface IStore\n{\n    public int Load();\n}'],
            ['Store.cs', 'public sealed class Store : IStore\n{\n    public int Load() => Helper();\n    private int Helper() => 1;\n    public int Orphan() => 2;\n}'],
            ['client.js', 'store.Load();']
        ];
        expect([...csUnreferenced(sources).keys()]).toEqual(['Orphan']);
    });

    it('reports two unused overloads declared in one file', () => {
        const sources = [
            ['Lib.cs', 'public static class Lib\n{\n    public static int Parse(int a) => a;\n    public static int Parse(string a) => 0;\n}'],
            ['Use.cs', 'var lib = typeof(Lib);']
        ];
        expect([...csUnreferenced(sources).keys()]).toEqual(['Parse']);
    });
});

describe('jsUnused', () => {
    it('reports a function named only at its declaration and in its own export entry', () => {
        const code = 'window.Lib = { used: used, orphan: orphan };\nfunction used() {}\nfunction orphan() {}';
        const unused = jsUnused([['lib.js', code]], `${code}\nwindow.Lib.used();`, babel);
        expect(unused).toEqual([{ file: 'lib.js', line: 3, name: 'orphan' }]);
    });

    it('reports a function assigned to a variable, arrow or expression (A-06.a)', () => {
        const code = 'const used = () => 1;\nconst arrow = (x) => x;\nvar expr = function () {};\nused();';
        expect(jsUnused([['lib.js', code]], code, babel)).toEqual([
            { file: 'lib.js', line: 2, name: 'arrow' },
            { file: 'lib.js', line: 3, name: 'expr' }
        ]);
    });
});

describe('cssUnreferenced', () => {
    it('accepts a class a script names or builds from a prefix, and reports the others', () => {
        const css = '.jux-card { margin: 0; }\n.jux-state-error { color: inherit; }\n/* .jux-in-comment */\n.jux-orphan { margin: 0; }';
        const users = "el.className = 'jux-card'; el.classList.add('jux-state-' + kind);";
        expect(cssUnreferenced([['a.css', css]], users)).toEqual([{ file: 'a.css', name: 'jux-orphan' }]);
    });
});

describe('i18nProblems', () => {
    it('reports a key missing from a language and an unreferenced non-client key', () => {
        const en = { 'admin.title': 'Title', 'admin.warning.Missing': 'x', 'admin.orphan': 'x', 'client.only': 'x', 'admin.enOnly': 'x' };
        const fr = { 'admin.title': 'Titre', 'admin.warning.Missing': 'x', 'admin.orphan': 'x', 'client.only': 'x' };
        const production = "t('admin.title'); t('admin.warning.' + code); t('admin.enOnly');";
        expect(i18nProblems(en, fr, production)).toEqual([
            'admin.enOnly is in en.json, not in fr.json.',
            'admin.orphan is named by no production file.'
        ]);
    });
});

describe('builtFromPrefix', () => {
    it('needs a literal prefix of at least two parts', () => {
        expect(builtFromPrefix('jux-state-error', '-', "'jux-state-' + kind")).toBe(true);
        expect(builtFromPrefix('jux-state-error', '-', "'jux-' + kind")).toBe(false);
    });
});

describe('checkAllowlist', () => {
    it('fails on an unlisted candidate and warns on a listed name that has callers again', () => {
        const result = checkAllowlist(
            new Map([['GetSections', ['Controller.cs']], ['Orphan', ['A.cs']]]),
            { 'Controller.cs:GetSections': 'route', 'B.cs:Revived': 'route' });
        expect(result.errors).toEqual([expect.stringContaining('Orphan (A.cs)')]);
        expect(result.warnings).toEqual([expect.stringContaining('B.cs:Revived')]);
    });

    it('lists a name per declaring file, so a new dead homonym elsewhere is not covered (A-06.a)', () => {
        const result = checkAllowlist(
            new Map([['GetSection', ['Controller.cs', 'Widgets/Other.cs']]]),
            { 'Controller.cs:GetSection': 'route' });
        expect(result.errors).toEqual([expect.stringContaining('GetSection (Widgets/Other.cs)')]);
    });
});
