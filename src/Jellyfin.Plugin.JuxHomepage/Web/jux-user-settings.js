'use strict';

// The JellyUX block of the native Preferences > Home page (#/mypreferenceshome), TODO_V4.md 8.3,
// F-36: a user turns JellyUX off for themselves, or hides home sections. Built from the page's own
// classes (sections, titles, checkboxes, descriptions, measured live on 10.11.10), so it reads like
// the rest of the page in both themes. Its choices are saved by the page's own Save button: the
// block listens to the form's submit, as the native fields do.
//
// While JellyUX is on, the ten native "Home screen section" selects have no effect (JellyUX renders
// the home screen), so they are hidden and the block says what replaces them; they come back as
// soon as the user unticks JellyUX.
//
// It sends a request only on this page: GET JuxHomepage/UserSettings when the page is shown, and
// POST on Save.
(function () {
    if (typeof window.juxUserSettings !== 'undefined') {
        return;
    }

    var _attempts = 0;

    window.juxUserSettings = {
        init: function () {
            document.addEventListener('viewshow', function () {
                _attempts = 0;
                _tryRender();
            });
            _tryRender();
        }
    };

    function _isHomePreferences() {
        return /^#\/mypreferenceshome(\?|$)/.test(window.location.hash);
    }

    // The preferences page may belong to another user when an administrator opens it from the
    // users dashboard (?userId=); the server's IDOR guard lets only administrators do so.
    function _pageUserId() {
        var match = /[?&]userId=([^&]+)/.exec(window.location.hash);
        return match ? decodeURIComponent(match[1]) : window.ApiClient.getCurrentUserId();
    }

    // The native form fills its fields after the view is shown; the block waits for the home
    // section selects, then renders once per page show.
    function _tryRender() {
        if (!_isHomePreferences() || !window.ApiClient) {
            return;
        }

        var page = document.querySelector('.userPreferencesPage:not(.hide)');
        var form = page && page.querySelector('form');
        if (!form || !form.querySelector('#selectHomeSection1')) {
            if (_attempts++ < 25) {
                setTimeout(_tryRender, 200);
            }
            return;
        }

        if (form.querySelector('.jux-user-settings')) {
            return;
        }

        var userId = _pageUserId();
        var lang = window.JuxCore.resolveLang();
        var url = window.ApiClient.getUrl('JuxHomepage/UserSettings', { userId: userId, lang: lang });
        window.ApiClient.getJSON(url).then(function (settings) {
            if (!form.isConnected || form.querySelector('.jux-user-settings')) {
                return;
            }
            _render(form, settings, userId, window.JuxCore.labels('usersettings', lang));
        }).catch(function (err) {
            console.error('[JellyUX] Failed to load the JellyUX settings:', err);
        });
    }

    function _checkboxHtml(inputClass, label, checked, attributes) {
        return '<label class="checkboxContainer emby-checkbox-label">' +
            '<input type="checkbox" is="emby-checkbox" class="emby-checkbox ' + inputClass + '"' +
            (checked ? ' checked' : '') + (attributes || '') + ' />' +
            '<span class="checkboxLabel">' + window.JuxCore.escHtml(label) + '</span>' +
            '<span class="checkboxOutline">' +
            '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span>' +
            '<span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>' +
            '</span></label>';
    }

    function _blockHtml(settings, t) {
        var sections = settings.Sections || [];
        var html = '<div class="verticalSection verticalSection-extrabottompadding jux-user-settings">' +
            '<h2 class="sectionTitle">' + window.JuxCore.escHtml(t.title) + '</h2>' +
            '<div class="checkboxContainer-withDescription">' +
            _checkboxHtml('jux-user-enabled', t.enabled, settings.Enabled !== false) +
            '<div class="fieldDescription">' + window.JuxCore.escHtml(t.enabledHelp) + '</div>' +
            '</div>' +
            '<fieldset class="jux-user-sections">' +
            '<legend class="sectionTitle jux-user-sections-title">' + window.JuxCore.escHtml(t.sections) + '</legend>';

        if (sections.length === 0) {
            html += '<div class="fieldDescription">' + window.JuxCore.escHtml(t.empty) + '</div>';
        } else {
            sections.forEach(function (section) {
                html += _checkboxHtml('jux-user-section', section.Name, !section.Hidden,
                    ' data-jux-section="' + window.JuxCore.escHtml(section.Key) + '"');
            });
            html += '<div class="fieldDescription">' + window.JuxCore.escHtml(t.sectionsHelp) + '</div>';
        }

        return html + '</fieldset>' +
            '<div class="fieldDescription jux-user-settings-error" role="alert" hidden>' + window.JuxCore.escHtml(t.saveFailed) + '</div>' +
            '</div>';
    }

    // The native "Home screen section N" selects, which JellyUX replaces while it is on.
    function _nativeSectionSelects(form) {
        return Array.prototype.map.call(
            form.querySelectorAll('select[id^="selectHomeSection"]'),
            function (select) { return select.closest('.selectContainer'); }
        ).filter(Boolean);
    }

    function _syncEnabled(form, block) {
        var enabled = block.querySelector('.jux-user-enabled').checked;
        var sections = block.querySelector('.jux-user-sections');
        sections.disabled = !enabled;
        sections.classList.toggle('jux-user-sections-off', !enabled);
        _nativeSectionSelects(form).forEach(function (container) {
            container.classList.toggle('hide', enabled);
        });
    }

    function _render(form, settings, userId, t) {
        var anchor = form.querySelector('#selectHomeSection1').closest('.verticalSection');
        var wrapper = document.createElement('div');
        wrapper.innerHTML = _blockHtml(settings, t);
        var block = wrapper.firstChild;
        anchor.parentNode.insertBefore(block, anchor.nextSibling);

        var enabledBox = block.querySelector('.jux-user-enabled');
        enabledBox.addEventListener('change', function () { _syncEnabled(form, block); });
        _syncEnabled(form, block);

        form.addEventListener('submit', function () {
            _save(block, userId);
        });
    }

    function _save(block, userId) {
        var enabled = block.querySelector('.jux-user-enabled').checked;
        var hidden = Array.prototype.filter.call(
            block.querySelectorAll('.jux-user-section'),
            function (box) { return !box.checked; }
        ).map(function (box) { return box.getAttribute('data-jux-section'); });
        var error = block.querySelector('.jux-user-settings-error');
        error.hidden = true;

        return window.ApiClient.ajax({
            type: 'POST',
            url: window.ApiClient.getUrl('JuxHomepage/UserSettings', { userId: userId }),
            data: JSON.stringify({ Enabled: enabled, HiddenSections: hidden }),
            contentType: 'application/json'
        }).then(function () {
            if (userId === window.ApiClient.getCurrentUserId()) {
                window.JuxCore.setUserEnabled(userId, enabled);
                _refreshHome();
            }
        }).catch(function (err) {
            console.error('[JellyUX] Failed to save the JellyUX settings:', err);
            error.hidden = false;
        });
    }

    // Jellyfin keeps the home view once rendered and only refreshes its rows on the way back, so the
    // saved choice would show after a page reload only (8096, 2026-10-13: no Sections request on
    // return). The home tab rebuilds its sections on "settingschange" (hometab chunk, 10.11.10).
    function _refreshHome() {
        Array.prototype.forEach.call(document.querySelectorAll('.sections'), function (sections) {
            sections.dispatchEvent(new CustomEvent('settingschange'));
        });
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { _blockHtml: _blockHtml, _render: _render, _save: _save, _pageUserId: _pageUserId };
    }

    window.juxUserSettings.init();
})();
