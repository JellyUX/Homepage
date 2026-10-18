// Reliability check: breadcrumb after detail X -> home -> detail X on the disposable 8098 bench.
//   node bench/reliability-breadcrumb.mjs
import { chromium } from '@playwright/test';
import { token, call, env } from './reliability-api.mjs';

const { token: tok, userId } = await token('admin');
const movies = JSON.parse((await call('GET', `/Items?recursive=true&includeItemTypes=Movie&userId=${userId}`, { tok })).text).Items;
const [x, y] = movies;

const browser = await chromium.launch();
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
await page.waitForTimeout(2000);

const state = () => page.evaluate(() => {
    const el = document.querySelector('.jux-breadcrumb');
    return el ? { visible: el.style.display !== 'none' && el.offsetParent !== null, text: el.textContent.trim().slice(0, 60) } : { visible: false, text: null };
});
const go = async (hash) => { await page.evaluate((h) => { location.hash = h; }, hash); await page.waitForTimeout(3000); };

await go(`#/details?id=${x.Id}`);
console.log(`1. detail X: ${JSON.stringify(await state())}`);
await go('#/home');
console.log(`2. home: ${JSON.stringify(await state())}`);
await go(`#/details?id=${x.Id}`);
console.log(`3. detail X again: ${JSON.stringify(await state())}`);
await go(`#/details?id=${y.Id}`);
console.log(`4. detail Y: ${JSON.stringify(await state())}`);
await go(`#/details?id=${x.Id}`);
console.log(`5. detail X after Y: ${JSON.stringify(await state())}`);
await browser.close();
