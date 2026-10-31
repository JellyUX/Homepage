import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const WEB = fileURLToPath(new URL('./src/Jellyfin.Plugin.JuxHomepage/Web/', import.meta.url));

// TODO_V2.md Phase 15.3: jsdom is needed only for the small handful of pure/near-pure functions in
// jux-homepage.js that touch window.location/document (see test/jux-homepage.test.js). The plugin
// has no bundler and no other front-end test surface today.
export default defineConfig({
    resolve: {
        // The admin page modules import each other as the plugin pages Jellyfin serves them as
        // (./configurationpage?name=juxhomepageconfig<part>js, Plugin.GetPages): here, the file Web/config-<part>.js.
        alias: [{ find: /^\.\/configurationpage\?name=juxhomepageconfig([a-z]+)js$/, replacement: WEB + 'config-$1.js' }]
    },
    test: {
        environment: 'jsdom',
        // test/ only: e2e/ holds the Playwright browser tests (npx playwright test -c e2e), and the
        // untracked folders of a local checkout hold other projects' suites.
        include: ['test/**/*.test.js'],
        setupFiles: ['test/setup-core.js'],
        // npx vitest run --coverage; summarized in CI by ci/coverage-summary.mjs, no threshold. lcov gives
        // the per-line hits of the changed lines coverage.
        coverage: {
            provider: 'v8',
            include: ['src/Jellyfin.Plugin.JuxHomepage/Web/*.js'],
            // A template the server fills in before splicing it into the home chunk: v8 cannot parse it as
            // it is. ci/coverage-summary.mjs lists it as not instrumented.
            exclude: ['src/Jellyfin.Plugin.JuxHomepage/Web/jux-loadsections-inject.js'],
            reporter: ['text-summary', 'json-summary', 'lcov', 'html'],
            reportsDirectory: 'coverage/js'
        }
    }
});
