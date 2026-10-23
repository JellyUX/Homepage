# Contributing to JellyUX Homepage

Thank you for your interest in contributing. This document covers the development setup, conventions, and workflow.

---

## Development Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (x64)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Git

---

## Environment Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/JellyUX/Homepage.git
   cd Homepage
   ```

2. Start the test Jellyfin server:
   ```bash
   docker compose -f docker/docker-compose.yml up -d
   ```
   Copy `docker/.env.example` to `.env` in the same folder and adjust the media paths before the first run.

3. Build and deploy the plugin to the test container (PowerShell):
   ```powershell
   .\docker\scripts\deploy-plugin.ps1
   ```
   This builds the plugin, copies the DLL to the container's plugin directory, and restarts Jellyfin.

4. Open `http://localhost:8096` to verify the plugin is loaded.

---

## Code Conventions

- **Language**: All code, variable names, comments, commit messages, and documentation must be written in **English**.
- **Commits**: Follow [Conventional Commits](https://www.conventionalcommits.org/):
  - Types: `feat`, `fix`, `refactor`, `style`, `docs`, `chore`, `test`
  - One atomic commit per logical change
  - Example: `feat: add ContinueWatching widget`
- **No commented-out code**: Dead code must be deleted, not commented.
- **CSS prefix**: All plugin CSS classes must use the `jux-` prefix.
- **CSS variables**: Reuse native Jellyfin CSS variables (`var(--primaryTextColor)`, `var(--accent)`, etc.) instead of hardcoding colors.

---

## Design Rules - No AI Slope

The following design elements are **strictly forbidden** in any contributed code, markup, or assets:

### Banned fonts

Inter, Poppins, Manrope, Outfit, Plus Jakarta Sans, Space Grotesk

### Banned colors and patterns

- Blue-violet or violet-cyan gradients
- Tailwind Blue `#3B82F6`
- Background Gray-50 `#F9FAFB`
- Black + neon violet combination
- Turquoise accent on dark background

### Typography

- No em dashes (the long dash character). Use regular hyphens or restructure the sentence.

---

## Adding a Widget

1. Extend the base class matching your widget's category rather than implementing `IWidget`
   directly:
   - `NativeWidgetBase` for `Widgets/Native/`
   - `AdminWidgetBase` (or `PersonWidgetBase` for actor/director widgets) for `Widgets/Admin/`
   - `PersonalizedWidgetBase` (or `PersonalizedPersonWidgetBase`) for `Widgets/Personalized/`
   - `ConnectedWidgetBase<T>` for `Widgets/Connected/` (TMDb-backed widgets)
2. Name the `WidgetType` following the `jux.<category>.<kebab-case-name>` convention (e.g.
   `jux.connected.discover-movies`).
3. Register it in `PluginServiceRegistrator.RegisterServices()` via the `RegisterWidget<TWidget>()`
   helper, used for every category, in the position matching the desired display order.
4. Add a localization key for both `fr.json` and `en.json` under `src/.../Localization/`, using the
   `WidgetType` string itself as the key. Personalized widgets also need a `.format` key (e.g.
   `jux.personalized.favorite-genre.format` = "More {value}").
5. For Connected/TMDb widgets: don't call the TMDb API directly from the widget. Read from
   `ITMDbCacheService`, which is refreshed separately by a scheduled task, and requires a TMDb API key
   configured in the plugin settings.
6. Document the widget in the README widget table and in a page under `docs/widgets/`
   (`ReadmeWidgetTableSyncTests` fails until both match the registrar).

---

## Adding a Language

Every label lives in `src/Jellyfin.Plugin.JuxHomepage/Localization/<lang>.json` (`en.json`, `fr.json`):
widget names, the admin page (`admin.*`) and the client scripts (`client.*`). To add a language, copy
`en.json` to `<lang>.json` (the short code of the web client language, e.g. `de.json`) and translate the
values; a key you leave out falls back to English. Every embedded file is picked up as a language, and
the client scripts receive the `client.*` keys through `jux-i18n.js`. Never write a label in a script.

## Testing

**Automated tests** (the same commands as the CI):
```bash
dotnet test --filter "Category!=RequiresDocker"   # server (xUnit)
npx vitest run                                    # client scripts
node ci/static-checks.mjs                         # project rules, duplication
node e2e/setup.mjs up; npx playwright test -c e2e # end to end, disposable server on 8098
```
Versioned documents are checked too: every symbol, repository path and `jux.*` key cited between
backticks in `README.md`, `CONTRIBUTING.md` and `docs/` must exist (`DocSymbolSyncTests`).

**Manual test procedure**:
1. Run `.\docker\scripts\deploy-plugin.ps1` to deploy the latest build.
2. Open `http://localhost:8096` and verify the home screen loads correctly.
3. Check the browser console for errors.
4. Test widget configuration from the admin panel.

## Failure Paths

Each boundary where the plugin meets something it does not control has a model test: copy its shape
when you touch that boundary, and add a row (with its test) for a new one. A test drives the failure
deterministically (a fake that throws, a corrupt file in a temporary directory, a stub HTTP handler, a
promise that never settles, a gate released by the test), never a sleep or a stopwatch.

| Boundary | Model test | What it proves |
|---|---|---|
| File system call that throws (`IFileSystem`) | `TransformationPatchesTests.FindLoadSectionsChunks_UnreadableChunk_IsSkippedAndTheOthersStillScanned` | the unreadable file is skipped, the others are still read |
| Corrupt file on disk, temporary directory | `UserConfigurationStoreTests.GetUserConfiguration_CorruptFile_ReturnsNullAndTheNextSaveReplacesIt` | a truncated JSON reads as no overrides, the next save replaces it (the plugin XML is read by Jellyfin, not by the plugin) |
| HTTP 401 and 429 (stub `HttpMessageHandler`) | `TMDbApiClientTests.GetTrendingMoviesAsync_Unauthorized_ReturnsEmptyWithoutRetry`, `WikidataApiClientTests.GetAwardWinnersAsync_RateLimited_ReturnsEmptyWithoutRetry` | an empty result after one call, no retry |
| External service that never answers (stub `HttpMessageHandler`, `ManualTimeProvider`) | `JuxHomepageControllerTests.SearchTMDbPerson_TMDbNeverAnswers_AnswersEmptyAtTheInteractiveDeadline`, `JuxHomepageControllerTests.SearchRewardsCeremony_WikidataNeverAnswers_AnswersEmptyAtTheInteractiveDeadline` | an admin lookup answers empty at the 8 s deadline instead of the HTTP client timeout; a request the browser aborts is still cancelled |
| Caller that cancels (stub `HttpMessageHandler`) | `TMDbApiClientTests.GetTrendingMoviesAsync_CallerCancels_ThrowsWithoutRetryOrCountingAFailure`, `WikidataApiClientTests.SearchEntitiesAsync_CallerCancels_Throws` | the cancellation reaches the caller, is not retried and does not count toward the TMDb circuit; an `HttpClient` timeout is still a failure |
| Request that never settles (client) | `test/jux-ui.test.js`, "shows the error state with a retry that renders again, never the empty state" | partial: a retry that never settles keeps the error state; the client has no request timeout to drive with fake timers yet |
| Observer re-armed after a reset (client) | `test/jux-tab-injector.test.js`, "re-injects into the tab bar rebuilt for a new view, as soon as the view is shown (F-42)" | partial: re-armed by `viewshow`, not after an explicit teardown |
| Admin round trip | `e2e/tests/admin.spec.mjs`, "the admin page keeps Save in view and moves a section by drag and drop or by position (F-50)" | saved, read back from the API, shown on the home page; the original configuration is restored |
| Drift of the Jellyfin web client | `test/native-selectors.test.js`, `JellyfinWebDriftTests.EveryInventoriedNativeToken_AppearsInTheTargetBundle`, `JellyfinWebDriftTests.EveryInventoriedCarrier_IsSetOnItsElementInTheTargetBundle`, `JellyfinWebDriftTests.EveryInventoriedAttributeValue_IsSetInTheTargetBundle` | every native token the scripts use is inventoried and present in the target bundle; a document-level class is read on the element the bundle sets it on; an attribute value a selector matches on is set by the bundle |
| Disk that refuses a write or a directory (`FailingFileSystem`) | `PerUserComputedCacheTests.Get_WhenTheDiskRefusesTheWrite_ServesTheComputedListAndComputesOncePerTtl`, `JuxUserSettingsControllerTests.Save_WhenTheSettingsCannotBeWritten_Answers500`, `DiskJsonCacheTests.Constructor_WhenTheDirectoryCannotBeCreated_BuildsADegradedCacheThatRefusesWrites` | a refused write is reported (500 for a user save), a computed list is kept in memory for one time-to-live, an unwritable data directory does not stop the services |
| Home chunk the server could not patch (client) | `test/jux-homepage.test.js`, "asks the server before reloading, and reloads only when it patches the home chunk (FIAB-04)" | the stale chunk guard reloads only for a chunk the server patched, and does nothing when the server does not answer |
| Refresh racing an update (`TaskCompletionSource`) | `CollectionsIndexCacheServiceTests.Invalidate_DuringARefresh_LeavesTheIndexStale` | a change made while the index is computed is not lost when it is written |

Time-dependent code takes a `TimeProvider`; tests pass the test project's `ManualTimeProvider` and call
`Advance` to fire its timers, and await a signal for background work (`ScoringService.WhenIdleForTesting`,
a `TaskCompletionSource` set by a mock).

## Measurement Rules

- **No time threshold in the CI.** CI runners are too noisy for a stopwatch: the CI checks numbers that
  do not depend on the machine (calls to the library, queries, requests, bytes), held by budgets that
  only go down.
- **Durations on the bench, before and after.** Measure with the `bench/` scripts (see
  `bench/README.md`), on the same machine, with the same data and nothing else running, and report the
  median and the p95. A bench whose files sit on a Windows bind mount (NTFS) is slower than a Docker
  named volume: compare a change only with a run on the same storage.
- **The user-facing metric.** An optimization reports the metric a user sees (home page ready, endpoint
  latency, task duration) before and after, not only the local counter it targets.
- **A failed measurement is investigated.** Before blaming the environment, break the time down per
  step, with figures. A value that looks wrong is recorded as a finding to triage, never accepted as a
  limit.

## Change Checklist

Each change goes through these steps, in order:

1. **Criteria first**: write one observable sentence per criterion, and how its test would fail.
2. **Instruments**: add the counters, test doubles or budgets the criteria need, at the current value.
3. **Fix**: a test that fails on the current code, then the code, then the same test passing. A budget
   lowered by the fix is lowered in the same commit.
4. **Targeted checks**: the tests of the touched area, `node ci/static-checks.mjs`.
5. **Bench**: deploy the DLL, replay the relevant `bench/` scripts and the E2E suite, desktop and 390 px
   width for any UI change, then restore the bench state.
6. **Review**: a fresh-context review of the diff against the criteria.
7. **Push**: only once the bench run passed (every green `feat:`/`fix:` push publishes a release).

## Definition of Done

A change is done when:

1. its criteria were written before the code;
2. each criterion has a test that failed first, at the level of the cost or the failure, and every
   boundary it touches has a failure-path test;
3. no budget went up (a raise needs a `Budget: <reason>` commit trailer);
4. every CI job is green;
5. a replayable bench script confirms it, the bench state is restored afterwards;
6. a performance change reports the user-facing metric before and after, not only the local one;
7. an independent review found nothing blocking, and similar code was searched for the same defect;
8. documentation and every language file are updated in the same commit.

---

## Jellyfin Update Procedure

JellyUX splices its home sections into a minified Jellyfin Web chunk. The splice depends on one
hardcoded minified name, `HookCardBuilder` in `Inject/TransformationPatches.cs`, and every native class,
id, attribute and custom element the client scripts use is listed in `Web/jux-native-selectors.json`,
with the element a document-level class is set on (`carriers`: `layout-tv` on `<html>`) and the attribute
values a selector matches on (`attributeValues`: `[data-id="resume"]` of the action sheet).
Any Jellyfin Web build can rename them, so run this procedure after every Jellyfin server update:

1. **Drift checks**: extract `/jellyfin/jellyfin-web` from the new image (as the `drift` job of
   `.github/workflows/ci.yml` does), set `JELLYFIN_WEB_EXTRACT_PATH` to it and run
   `dotnet test --filter "Category=RequiresDocker"` (`JellyfinWebDriftTests`: loadSections splice,
   home-html anchor, every token, carrier and attribute value of `Web/jux-native-selectors.json`).
2. **E2E suite**: `node e2e/setup.mjs up --image jellyfin/jellyfin:<version>`, then
   `npx playwright test -c e2e`.
3. **Server log**: bump the image in `docker/docker-compose.yml`, run
   `docker compose -f docker/docker-compose.yml up -d`, and read the log after the restart. It must show
   `Found loadSections chunk: {FileName}.`, then `Registered loadSections chunk transformation for
   {FileName}` and `Found home-html tab anchor in {FileName}.` A warning that points to this procedure
   means the home page or the tab panes fall back to native rendering: a degradation, not a crash. The
   same state is on the admin page banner (`HomeHookNotFound`, `HomeChunkMissing`, `TabsAnchorMissing`)
   and in `GET JuxHomepage/PatchStatus`, which must answer `{"LoadSections":"Patched","TabsAnchor":true}`.
4. **Renamed cardBuilder module**: update `HookCardBuilder` in `Inject/TransformationPatches.cs`. In the
   chunk found in step 3, find the webpack module that defines `,loadSections:` and the name it binds to
   the cardBuilder module (`,X=r(id)`, read as `X.default.getCardsHtml`).
5. **Missing native token, moved carrier or attribute value**: adapt the script that uses it, then
   `Web/jux-native-selectors.json` (`test/native-selectors.test.js` fails until both agree). Raise the E2E matrix and the `drift` job
   image to the new validated version.
6. **Home page**: if the JellyUX sections do not render, check the browser console (prefix `[JellyUX]`)
   before suspecting the minified hook constants. A drifted chunk shows "Jellyfin Web has changed:
   JellyUX went back to the native home screen" and never reloads the page; the cache message (CDN or
   proxy) only appears when `PatchStatus` says the server patched the chunk.

On a bench, never click **Disable** on JellyUX or File Transformation: Jellyfin 10.11 deletes a
disabled plugin at the next restart when an active plugin sorts after it by name
(`docs/troubleshooting.md`).

---

## Configuration Schema Versioning

`PluginConfiguration.SchemaVersion` and `UserConfiguration.SchemaVersion` gate one-time migrations of the
on-disk configuration format. They are two separate tracks, compared with `CurrentSchemaVersion` and
`CurrentUserSchemaVersion` in `Plugin.cs`.

- **Plugin configuration**: on load (the `Plugin` constructor forces the lazy `Configuration`), the
  `Plugin.MigrateConfiguration` tiers run, then `Plugin.ValidateConfiguration`; the file is saved at once
  when the version changed. On save, `Plugin.UpdateConfiguration` validates (clamps) the values but does
  not migrate: a configuration posted by the admin page is already at the current version.
- **User configuration**: `Plugin.MigrateUserConfiguration` runs in memory when a user file is read, and
  again before it is saved. It stamps the user constant, never the plugin one.
- **Tiers**: each tier is its own `if (config.SchemaVersion < N)` block, never `else if`, so an
  installation several versions behind applies every intermediate transformation in order. A new tier
  bumps the matching constant.
- **Widget rows**: a migration of widget rows transforms `PluginConfiguration.Widgets`.
  `UserConfiguration.WidgetOverrides` is frozen (QUAL-02): it shares the `WidgetConfig[]` structure and
  is still read from the user files, but the layout ignores it and no page writes it, so a new tier does
  not have to transform it. The existing tiers still do. Frozen with it: `WidgetConfig.AllowUserOverride`,
  `MinInstances` and `MaxInstances`, saved as loaded by the admin page (`ConfigurationEffectTests` fails
  if production code reads them again). Accepted risk: an override written by hand in a user file has
  no effect. Thawing them means restoring the double migration first.

---

## Submitting a Contribution

1. Fork the repository and create a branch from `main`.
2. Make your changes following the conventions above.
3. Open a pull request against `main` with a clear description of what changed and why.
4. Ensure the CI build passes before requesting review.

Note: this repository uses `main` as the only permanent branch. Feature branches exist only in forks.
