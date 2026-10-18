import { defineConfig } from '@playwright/test';

// Runs against the bench started by e2e/setup.mjs. The baseurl project needs the server moved under a
// BaseUrl first (node e2e/setup.mjs base-url /jf), the legacy-config project a configuration saved by an
// earlier release (node e2e/setup.mjs load-config e2e/fixtures/config-2.13.0.xml): each project runs as
// a separate invocation, in that order.
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
        { name: 'root', testIgnore: /(baseurl|legacy-config)\.spec/ },
        // A phone-sized viewport for the home page and the outage states (DEC-P03-4), next to root; the
        // 30 s idle detail page does not depend on the viewport and runs in root only.
        {
            name: 'mobile',
            testMatch: /(home|errors)\.spec/,
            grepInvert: /for 30 s/,
            use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }
        },
        { name: 'baseurl', testMatch: /baseurl\.spec/ },
        { name: 'legacy-config', testMatch: /legacy-config\.spec/ }
    ]
});
