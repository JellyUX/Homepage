// Reliability check: what a user sees on the home page of the 8098 bench (after a simulated drift,
// File Transformation removed, a reset configuration...). Counts page loads to detect the
// stale-chunk guard reload, and reports the JellyUX notice if one is shown.
//   node bench/reliability-home-state.mjs <label>
import { chromium } from '@playwright/test';
import { env } from './reliability-api.mjs';

const label = process.argv[2] ?? 'home';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
let loads = 0;
page.on('load', () => { loads++; });
const errors = [];
page.on('pageerror', (e) => errors.push(String(e).slice(0, 160)));
await page.goto(`${env.serverUrl}/web/`);
const name = page.locator('#txtManualName:visible');
const manual = page.locator('.btnManual:visible');
for (let i = 0; i < 60 && !(await name.isVisible()); i++) {
    if (await manual.isVisible()) await manual.click({ timeout: 2000 }).catch(() => {});
    await page.waitForTimeout(500);
}
await name.fill(env.admin.name);
await page.locator('#txtManualPassword:visible').fill(env.admin.password);
await page.locator('.manualLoginForm:visible .button-submit').click();
await page.waitForURL(/#\/home/, { timeout: 30000 }).catch(() => {});
const loadsAtLogin = loads;
await page.waitForTimeout(15000);
const s = await page.evaluate(() => ({
    patched: window.JuxLoadSectionsPatched === true,
    juxScripts: document.querySelectorAll('script[src*="JuxHomepage"]').length,
    juxSections: document.querySelectorAll('.jux-widget-section').length,
    nativeSections: document.querySelectorAll('#indexPage .sections > .verticalSection:not(.jux-widget-section)').length,
    juxTabs: document.querySelectorAll('[id^="jux-tab-"]').length,
    notice: document.getElementById('jux-chunk-notice')?.textContent ?? null
}));
console.log(`[${label}] loads after login=${loads - loadsAtLogin} ${JSON.stringify(s)} pageErrors=${errors.length}${errors[0] ? ' first=' + errors[0] : ''}`);
await browser.close();
