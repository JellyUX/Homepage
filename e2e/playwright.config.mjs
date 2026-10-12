import { defineConfig } from '@playwright/test';

// Runs against the bench started by e2e/setup.mjs. The baseurl project needs the server moved under a
// BaseUrl first (node e2e/setup.mjs base-url /jf), so the two projects run as separate invocations.
export default defineConfig({
    testDir: './tests',
    outputDir: './.work/results',
    timeout: 60000,
    expect: { timeout: 15000 },
    workers: 1,
    retries: 0,
    reporter: [['list'], ['html', { outputFolder: './.work/report', open: 'never' }]],
    use: {
        viewport: { width: 1920, height: 1080 },
        locale: 'en-US',
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure'
    },
    projects: [
        { name: 'root', testIgnore: /baseurl\.spec/ },
        { name: 'baseurl', testMatch: /baseurl\.spec/ }
    ]
});
