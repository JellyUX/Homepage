import { describe, it, expect, afterEach, vi } from 'vitest';
import juxUserSettings from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-user-settings.js';

const { _blockHtml, _render } = juxUserSettings;
const english = () => window.JuxCore.labels('usersettings', 'en');

// The part of the native Preferences > Home form the block anchors on (measured on 10.11.10).
function nativeForm() {
    document.body.innerHTML =
        '<div class="page userPreferencesPage"><form>' +
        '<div class="verticalSection verticalSection-extrabottompadding"><h2 class="sectionTitle">Home</h2>' +
        '<div class="selectContainer"><select id="selectHomeSection1"></select></div>' +
        '<div class="selectContainer"><select id="selectHomeSection2"></select></div>' +
        '</div>' +
        '<div class="verticalSection"><h2 class="sectionTitle">Library Order</h2></div>' +
        '<button type="submit" class="btnSave">Save</button>' +
        '</form></div>';
    return document.querySelector('form');
}

const settings = {
    Enabled: true,
    Sections: [
        { Key: 'jux.native.recently-added-shows#1', Name: 'Recently Added Shows', Hidden: true },
        { Key: 'abc', Name: '<b>Halloween</b>', Hidden: false }
    ]
};

describe('JellyUX block of Preferences > Home (F-36)', () => {
    afterEach(() => {
        delete window.ApiClient;
        window.sessionStorage.clear();
        document.body.innerHTML = '';
    });

    it('lists each section with its shown state, names escaped', () => {
        const html = _blockHtml(settings, english());

        expect(html).toContain('&lt;b&gt;Halloween&lt;/b&gt;');
        expect(html).not.toContain('<b>Halloween');
        const doc = new DOMParser().parseFromString(html, 'text/html');
        expect([...doc.querySelectorAll('.jux-user-section')].map((b) => b.checked)).toEqual([false, true]);
    });

    it('sits under the native Home section and hides the native section selects while JellyUX is on', () => {
        const form = nativeForm();

        _render(form, settings, 'u1', english());

        const block = form.querySelector('.jux-user-settings');
        expect(block.previousElementSibling.querySelector('#selectHomeSection1')).not.toBeNull();
        const selects = [...form.querySelectorAll('.selectContainer')];
        expect(selects.every((c) => c.classList.contains('hide'))).toBe(true);

        const enabled = block.querySelector('.jux-user-enabled');
        enabled.checked = false;
        enabled.dispatchEvent(new Event('change'));

        expect(selects.some((c) => c.classList.contains('hide'))).toBe(false);
        expect(block.querySelector('.jux-user-sections').disabled).toBe(true);
    });

    it('saves with the page Save button: the unticked sections and the JellyUX switch', async () => {
        const form = nativeForm();
        const ajax = vi.fn(() => Promise.resolve());
        window.ApiClient = { getUrl: (route, query) => route + '?userId=' + query.userId, getCurrentUserId: () => 'u1', ajax };
        _render(form, settings, 'u1', english());
        form.querySelector('.jux-user-enabled').checked = false;

        form.dispatchEvent(new Event('submit', { cancelable: true }));
        await new Promise((r) => setTimeout(r, 0));

        expect(ajax).toHaveBeenCalledTimes(1);
        expect(ajax.mock.calls[0][0].url).toBe('JuxHomepage/UserSettings?userId=u1');
        expect(JSON.parse(ajax.mock.calls[0][0].data)).toEqual({ Enabled: false, HiddenSections: ['jux.native.recently-added-shows#1'] });
        expect(window.JuxCore.isOffForUser()).toBe(true);
    });

    it('has the kept home view rebuild its sections after a save, not only after a reload', async () => {
        const form = nativeForm();
        const home = document.createElement('div');
        home.className = 'sections';
        document.body.appendChild(home);
        const rebuild = vi.fn();
        home.addEventListener('settingschange', rebuild);
        window.ApiClient = { getUrl: (route) => route, getCurrentUserId: () => 'u1', ajax: () => Promise.resolve() };
        _render(form, settings, 'u1', english());

        form.dispatchEvent(new Event('submit', { cancelable: true }));
        await new Promise((r) => setTimeout(r, 0));

        expect(rebuild).toHaveBeenCalledTimes(1);
    });

    it('says when the save failed', async () => {
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const form = nativeForm();
        window.ApiClient = { getUrl: (route) => route, getCurrentUserId: () => 'u1', ajax: () => Promise.reject(new Error('offline')) };
        _render(form, settings, 'u1', english());

        form.dispatchEvent(new Event('submit', { cancelable: true }));
        await new Promise((r) => setTimeout(r, 0));

        expect(form.querySelector('.jux-user-settings-error').hidden).toBe(false);
        errorSpy.mockRestore();
    });
});
