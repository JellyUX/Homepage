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
        // A phone-sized viewport, next to root, for the cases whose rendering depends on it (DEC-P03-4,
        // DEC-P03-12): the home sections, the tabs, the outage states and the detail header (A-10).
        // Request budgets and navigation do not depend on the viewport and run in root only.
        {
            name: 'mobile',
            testMatch: /(home|errors|featured|navigation)\.spec/,
            grep: /F-25|one pane at a time|F-40|KTW-04|KTW-01\.a banner|FIAB-01\.a|A-10/,
            use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true }
        },
        { name: 'baseurl', testMatch: /baseurl\.spec/ },
        { name: 'legacy-config', testMatch: /legacy-config\.spec/ }
    ]
});
