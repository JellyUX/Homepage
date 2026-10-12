import { configDefaults, defineConfig } from 'vitest/config';

// TODO_V2.md Phase 15.3: jsdom is needed only for the small handful of pure/near-pure functions in
// jux-homepage.js that touch window.location/document (see test/jux-homepage.test.js). The plugin
// has no bundler and no other front-end test surface today.
export default defineConfig({
    test: {
        environment: 'jsdom',
        // e2e/ holds the Playwright browser tests (npx playwright test -c e2e).
        exclude: [...configDefaults.exclude, 'e2e/**'],
        setupFiles: ['test/setup-core.js'],
        // npx vitest run --coverage; summarized in CI by ci/coverage-summary.mjs, no threshold.
        coverage: {
            provider: 'v8',
            include: ['src/Jellyfin.Plugin.JuxHomepage/Web/*.js'],
            reporter: ['text-summary', 'json-summary', 'html'],
            reportsDirectory: 'coverage/js'
        }
    }
});
