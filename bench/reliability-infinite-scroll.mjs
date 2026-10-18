// Reliability check (FIAB-01, infinite scroll): opens the Movies library (4 movies, all on the native first
// page) on the disposable 8098 bench and counts the JellyUX infinite-scroll requests
// (Users/{id}/Items with a StartIndex) over a fixed window. A finished list should send at most one.
//   node bench/reliability-infinite-scroll.mjs [windowMs]
import { chromium } from '@playwright/test';
import { env } from './reliability-api.mjs';
const windowMs = Number(process.argv[2] ?? 15000);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
const hits = [];
page.on('request', (r) => {
    const u = r.url();
    if (/\/Users\/[0-9a-f]+\/Items\?/i.test(u) && /StartIndex=/i.test(u)) hits.push({ t: Date.now(), u: u.replace(env.serverUrl, '') });
});
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

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
await page.waitForTimeout(3000);
const before = hits.length;
await page.locator('.jux-widget-jux-native-recently-added-movies .sectionTitleContainer a').click();
await page.waitForTimeout(1500);
await page.evaluate(() => { location.hash = location.hash.replace('tab=1', 'tab=0'); });
await page.waitForTimeout(2000);
const t0 = Date.now();
const startCount = hits.length;
await page.waitForTimeout(windowMs);
const inWindow = hits.filter((h) => h.t >= t0);
console.log('dom=' + JSON.stringify(await page.evaluate(() => ({ moviesTab: !!document.querySelector('.libraryPage:not(.hide) #moviesTab.is-active'), activeTabs: [...document.querySelectorAll('.libraryPage:not(.hide) .pageTabContent.is-active')].map(e => e.id), sentinel: document.querySelectorAll('.jux-infinite-sentinel').length, cardHooks: !!document.querySelector('script[src*="jux-card-hooks"]') }))));
console.log(`url=${page.url().replace(env.serverUrl, '')}`);
console.log(`cards=${await page.locator('.libraryPage:not(.hide) .itemsContainer .card').count()}`);
console.log(`requests before library=${before}, first 2 s=${startCount - before}, during ${windowMs} ms window=${inWindow.length}`);
console.log(`rate=${(inWindow.length / (windowMs / 1000)).toFixed(1)} req/s`);
for (const h of inWindow.slice(0, 3)) console.log(`sample: ${h.u.slice(0, 220)}`);
console.log(`console errors=${errors.length}${errors[0] ? ' first: ' + errors[0].slice(0, 200) : ''}`);
await browser.close();
