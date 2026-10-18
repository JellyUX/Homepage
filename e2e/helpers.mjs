import { readFileSync } from 'node:fs';
import { expect } from '@playwright/test';

export const env = JSON.parse(readFileSync(new URL('./.work/env.json', import.meta.url), 'utf8'));

// The seven native widgets a fresh install seeds (NativeWidgetDefaults), in layout order.
export const NATIVE_SECTIONS = [
    'jux-native-continue-watching',
    'jux-native-next-up',
    'jux-native-recently-added-movies',
    'jux-native-recently-added-shows',
    'jux-native-recently-added-episodes',
    'jux-native-my-media',
    'jux-native-watchlist'
];

export const TABS = [
    { index: 2, id: 'jux-tab-watchlist' },
    { index: 3, id: 'jux-tab-progress' },
    { index: 4, id: 'jux-tab-history' },
    { index: 5, id: 'jux-tab-statistics' }
];

const AUTH = 'MediaBrowser Client="jux-e2e", Device="ci", DeviceId="jux-e2e-api", Version="1.0.0"';

/** Signs in through the Jellyfin login form and waits for the home page. */
export async function login(page, user) {
    await page.goto(`${env.serverUrl}/web/`);
    // The web client can keep a hidden copy of the login view: act on the visible one only. The form
    // opens directly when no user is listed publicly; otherwise it sits behind a button.
    // The view can switch while it settles (seen on CI after a restart), so poll instead of trusting the
    // first element found, and give the button click a short timeout of its own.
    const name = page.locator('#txtManualName:visible');
    const manual = page.locator('.btnManual:visible');
    const deadline = Date.now() + 30000;
    while (!(await name.isVisible())) {
        if (Date.now() > deadline) {
            throw new Error('The manual login form never became visible.');
        }
        if (await manual.isVisible()) {
            await manual.click({ timeout: 2000 }).catch(() => {});
        }
        await page.waitForTimeout(500);
    }
    await name.fill(user.name);
    await page.locator('#txtManualPassword:visible').fill(user.password);
    await page.locator('.manualLoginForm:visible .button-submit').click();
    // A hash route change fires no load event, so waitForURL would never resolve.
    await expect(page).toHaveURL(/#\/home/);
}

/** Scrolls until the lazy loader stops adding sections, then returns their widget types in order. */
export async function loadedSections(page) {
    await page.locator('.homeSectionsContainer.jux-homepage-active .jux-widget-section').first().waitFor();
    let previous = -1;
    let count = await page.locator('.jux-widget-section').count();
    while (count !== previous) {
        previous = count;
        await page.mouse.wheel(0, 20000);
        await page.waitForTimeout(1500);
        count = await page.locator('.jux-widget-section').count();
    }
    return page.locator('.jux-widget-section').evaluateAll((sections) =>
        sections.map((s) => [...s.classList].find((c) => c.startsWith('jux-widget-jux-')).replace('jux-widget-', '')));
}

/** Calls the server API as the given user. */
export async function apiGet(user, route) {
    const auth = await fetch(`${env.serverUrl}/Users/AuthenticateByName`, {
        method: 'POST',
        headers: { Authorization: AUTH, 'Content-Type': 'application/json' },
        body: JSON.stringify({ Username: user.name, Pw: user.password })
    });
    expect(auth.ok).toBe(true);
    const { AccessToken } = await auth.json();
    const res = await fetch(`${env.serverUrl}${route}`, { headers: { Authorization: `${AUTH}, Token="${AccessToken}"` } });
    expect(res.ok).toBe(true);
    return res.json();
}

/**
 * Records, in order, the JellyUX API requests of a page (the client assets left out) and the native
 * library queries the client scripts issue (`Users/{id}/Items`, infinite scroll). A duplicate is the same
 * method and URL twice. `mark()` returns a position that `count` and `duplicates` can start from.
 */
export function trackJuxRequests(page) {
    const requests = [];
    page.on('request', (req) => {
        const url = new URL(req.url());
        const juxApi = url.pathname.includes('/JuxHomepage/') && !/\.(js|css)$/.test(url.pathname);
        const libraryQuery = /\/Users\/[0-9a-f-]+\/Items$/i.test(url.pathname);
        if (juxApi || libraryQuery) {
            requests.push({ method: req.method(), url: req.url(), path: url.pathname });
        }
    });
    return {
        mark: () => requests.length,
        since: (from = 0) => requests.slice(from),
        count: (pattern, from = 0) => requests.slice(from).filter((r) => pattern.test(r.path)).length,
        duplicates(from = 0) {
            const seen = new Set();
            return requests.slice(from).map((r) => `${r.method} ${r.url}`).filter((key) => seen.has(key) || !seen.add(key));
        }
    };
}

/** Collects JellyUX console errors and failed JellyUX asset requests for a page. */
export function watchJuxErrors(page) {
    const errors = [];
    page.on('console', (msg) => {
        if (msg.type() === 'error' && msg.text().includes('[JellyUX]')) {
            errors.push(msg.text());
        }
    });
    page.on('response', (res) => {
        if (res.url().includes('/JuxHomepage/') && res.status() >= 400) {
            errors.push(`${res.status()} ${res.url()}`);
        }
    });
    return errors;
}
