// Reliability check: does an unchanged admin Save keep the Rewards and Discover filters?
// Adds a Rewards row (ceremonyQid + year) and a Discover Movies row (personIds + genreIds) through the
// API, opens the configuration page, clicks Save without touching anything, then reads the rows back.
// Restores the original configuration at the end. Disposable 8098 bench only.
//   node bench/reliability-admin-roundtrip.mjs
import { chromium } from '@playwright/test';
import { token, call, env } from './reliability-api.mjs';

const PLUGIN = '3adf1f1f-1541-4e47-b9e3-34d2d2968af6';
const { token: tok } = await token('admin');
const original = JSON.parse((await call('GET', `/Plugins/${PLUGIN}/Configuration`, { tok })).text);
const cfg = structuredClone(original);
cfg.ApiKeys = { ...(cfg.ApiKeys || {}), TMDb: '' };
cfg.Widgets.push(
    { WidgetType: 'jux.connected.rewards', Enabled: true, Order: 900, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
        ExtraParams: [{ Key: 'value', Value: '11111111-2222-3333-4444-555555555555' }, { Key: 'ceremonyQid', Value: 'Q19020' }, { Key: 'year', Value: '2024' }] },
    { WidgetType: 'jux.connected.discover-movies', Enabled: true, Order: 910, MinItems: 1, MaxItems: 20, ViewMode: 'Portrait',
        ExtraParams: [{ Key: 'value', Value: '66666666-7777-8888-9999-000000000000' }, { Key: 'personIds', Value: '525' }, { Key: 'genreIds', Value: '18' }] }
);
console.log(`seed POST HTTP ${(await call('POST', `/Plugins/${PLUGIN}/Configuration`, { tok, body: cfg })).status}`);
const pick = (c) => c.Widgets.filter((w) => w.WidgetType === 'jux.connected.rewards' || w.WidgetType === 'jux.connected.discover-movies')
    .map((w) => `${w.WidgetType}: ${JSON.stringify(w.ExtraParams.filter((p) => p.Key !== 'value'))}`);
console.log('before Save:\n  ' + pick(JSON.parse((await call('GET', `/Plugins/${PLUGIN}/Configuration`, { tok })).text)).join('\n  '));

const browser = await chromium.launch();
try {
    const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
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
    const tCfg = Date.now();
    await page.goto(`${env.serverUrl}/web/#/configurationpage?name=JellyUX%20Homepage`);
    await page.locator('#juxHomepageConfigPage:visible .jux-admin-widget-card').first().waitFor({ timeout: 240000 });
    console.log(`config page cards visible after ${Date.now() - tCfg} ms (TMDb hosts blackholed: ${process.env.BLACKHOLE ?? '?'})`);
    await page.waitForTimeout(2000);
    const shown = await page.evaluate(() => [...document.querySelectorAll('.jux-rewards-ceremony, .jux-discover-person')].map((e) => `${e.className.split(' ').pop()}="${e.value}"`));
    console.log(`inputs shown on load: ${shown.join(', ')}`);
    const posts = [];
    page.on('request', (r) => { if (r.method() === 'POST' && /Plugins\/.*Configuration/.test(r.url())) posts.push(r.postData() ?? ''); });
    await page.locator('#juxHomepageConfigPage:visible .jux-admin-actions .button-submit').click();
    await page.waitForTimeout(3000);
    console.log(`config POSTs sent by Save: ${posts.length}; rewards Qid in body: ${posts.some((p) => p.includes('Q19020'))}`);
    await page.waitForTimeout(3000);
    console.log('after unchanged Save:\n  ' + pick(JSON.parse((await call('GET', `/Plugins/${PLUGIN}/Configuration`, { tok })).text)).join('\n  '));
} finally {
    await browser.close();
    console.log(`restore POST HTTP ${(await call('POST', `/Plugins/${PLUGIN}/Configuration`, { tok, body: original })).status}`);
}
