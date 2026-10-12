import { describe, it, expect, afterEach, vi } from 'vitest';
import juxCore from '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-core.js';

const { escHtml, cssUrl, iconName, cached, getItem, getIncludedIn, observeCardImages, errorHtml, showError } = juxCore;

describe('escHtml (F-35)', () => {
    it('escapes ampersands, angle brackets, double quotes and apostrophes', () => {
        expect(escHtml('<b>Tom & "Jerry"</b>')).toBe('&lt;b&gt;Tom &amp; &quot;Jerry&quot;&lt;/b&gt;');
        expect(escHtml("L'Odyssée")).toBe('L&#39;Odyssée');
    });

    it('keeps a single-quoted attribute closed on an apostrophe', () => {
        const div = document.createElement('div');
        div.innerHTML = "<span title='" + escHtml("x' onmouseover='alert(1)") + "'></span>";

        expect(div.firstChild.getAttribute('onmouseover')).toBeNull();
        expect(div.firstChild.getAttribute('title')).toBe("x' onmouseover='alert(1)");
    });

    it('returns an empty string for nullish input and keeps zero', () => {
        expect(escHtml('')).toBe('');
        expect(escHtml(null)).toBe('');
        expect(escHtml(undefined)).toBe('');
        expect(escHtml(0)).toBe('0');
    });
});

describe('cssUrl (F-35)', () => {
    it('encodes parentheses, quotes and spaces so the url() token stays closed', () => {
        expect(cssUrl("/Items/1/Images/Primary?tag=a(b)'c\" d")).toBe('url("/Items/1/Images/Primary?tag=a%28b%29%27c%22%20d")');
    });

    it('keeps existing percent escapes', () => {
        expect(cssUrl('/img/a%20b.jpg')).toBe('url("/img/a%20b.jpg")');
    });

    it('applies as a single background image in the DOM', () => {
        const el = document.createElement('div');
        el.style.backgroundImage = cssUrl('/x/a)b.jpg');

        expect(el.style.backgroundImage).toContain('a%29b.jpg');
    });
});

describe('iconName (F-35)', () => {
    it('accepts Material ligature names', () => {
        expect(iconName('bookmark_border')).toBe('bookmark_border');
    });

    it('falls back on anything that could add classes or attributes', () => {
        expect(iconName('info" onclick="x')).toBe('info');
        expect(iconName('a b')).toBe('info');
        expect(iconName(undefined, 'movie')).toBe('movie');
    });
});

describe('cached', () => {
    it('sends a request once per item and key, and again for another item', () => {
        let calls = 0;
        const request = () => { calls += 1; return Promise.resolve(calls); };

        cached('a', 'item', request);
        cached('a', 'item', request);
        cached('b', 'item', request);

        expect(calls).toBe(2);
    });

    it('lets collections and the breadcrumb share one getItem and one Included In per item', () => {
        window.ApiClient = {
            getUrl: path => path,
            getItem: vi.fn().mockResolvedValue({ Type: 'Movie' }),
            getJSON: vi.fn().mockResolvedValue([])
        };

        getItem('user1', 'shared1');
        getIncludedIn('shared1');
        getItem('user1', 'shared1');
        getIncludedIn('shared1');

        expect(window.ApiClient.getItem).toHaveBeenCalledTimes(1);
        expect(window.ApiClient.getJSON).toHaveBeenCalledTimes(1);
        expect(window.ApiClient.getJSON).toHaveBeenCalledWith('JuxHomepage/Collections/IncludedIn/shared1');
        delete window.ApiClient;
    });
});

describe('observeCardImages (F-10, F-35)', () => {
    const originalObserver = globalThis.IntersectionObserver;
    let observers = [];

    afterEach(() => {
        globalThis.IntersectionObserver = originalObserver;
        observers = [];
    });

    function installObserver() {
        globalThis.IntersectionObserver = class {
            constructor(callback) { this.callback = callback; this.targets = []; this.disconnected = false; observers.push(this); }
            observe(el) { this.targets.push(el); }
            unobserve(el) { this.targets = this.targets.filter(t => t !== el); }
            disconnect() { this.disconnected = true; this.targets = []; }
        };
    }

    function cards(count) {
        const container = document.createElement('div');
        container.innerHTML = Array.from({ length: count }, (_, i) =>
            '<div class="cardImageContainer lazy" data-src="/Items/' + i + '/Images/Primary"></div>').join('');
        return container;
    }

    it('applies only the images of cards that come into view', () => {
        installObserver();
        const container = cards(100);

        observeCardImages(container);
        const visible = observers[0].targets.slice(0, 10);
        observers[0].callback(visible.map(target => ({ target, isIntersecting: true })));

        expect(container.querySelectorAll('.cardImageContainer:not(.lazy)').length).toBe(10);
        expect(visible[0].style.backgroundImage).toContain('/Items/0/Images/Primary');
        expect(observers[0].targets.length).toBe(90);
    });

    it('drops the observer of the previous render of the same container', () => {
        installObserver();
        const container = cards(3);

        observeCardImages(container);
        container.innerHTML = cards(2).innerHTML;
        observeCardImages(container);

        expect(observers[0].disconnected).toBe(true);
        expect(observers[1].targets.length).toBe(2);
    });

    it('applies every image at once without IntersectionObserver', () => {
        globalThis.IntersectionObserver = undefined;
        const container = cards(3);

        observeCardImages(container);

        expect(container.querySelectorAll('.cardImageContainer.lazy').length).toBe(0);
    });
});

describe('error state (F-40)', () => {
    it('names what failed, says what to do and offers a native raised retry button', () => {
        document.documentElement.lang = 'en';
        const div = document.createElement('div');
        div.innerHTML = errorHtml({ name: '<Watchlist>' });

        expect(div.querySelector('[role="alert"]')).not.toBeNull();
        expect(div.querySelector('.jux-error-title').textContent).toBe('Couldn’t load <Watchlist>');
        expect(div.querySelector('.jux-error-subtitle').textContent).toContain('try again');
        expect(div.querySelector('button.raised.emby-button.jux-error-retry').textContent).toBe('Retry');
    });

    it('renders one line without subtitle inside a home section, in French', () => {
        document.documentElement.lang = 'fr-FR';
        const div = document.createElement('div');
        div.innerHTML = errorHtml({ compact: true });

        expect(div.querySelector('.jux-error-state-compact .jux-error-title').textContent).toBe('Impossible de charger cette section.');
        expect(div.querySelector('.jux-error-subtitle')).toBeNull();
        expect(div.querySelector('.jux-error-retry').textContent).toBe('Réessayer');
        document.documentElement.lang = 'en';
    });

    it('calls onRetry once and disables the button', () => {
        const container = document.createElement('div');
        const onRetry = vi.fn();
        showError(container, { name: 'History', onRetry });

        const button = container.querySelector('.jux-error-retry');
        button.click();
        button.click();

        expect(onRetry).toHaveBeenCalledTimes(1);
        expect(button.disabled).toBe(true);
    });
});
