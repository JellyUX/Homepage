# Bench scripts

Replayable measurements of a running Jellyfin server with JellyUX installed. They are not part of the CI:
the CI checks counts (calls, queries, bytes), never durations. Durations are measured here, before and
after a change, on the same machine with no other load (see `CONTRIBUTING.md` "Measurement Rules").

Requirements: Node.js with the repository's `npm ci` done (Playwright Chromium for the browser scripts),
`curl` and `docker` on the `PATH`, and the bench logins in `identifiants.txt` at the repository root
(gitignored, read at run time, never written).

| Variable | Default | Use |
|---|---|---|
| `JUX_URL` | `http://localhost:8096` | server to measure |
| `JUX_CONTAINER` | `jellyux-test` | its container, for restarts, `docker stats` and logs |
| `JUX_BENCH_OUT` | `bench/.work/raw` | folder the raw results are written to (not versioned) |
| `PERF_SCRATCH` | `JUX_BENCH_OUT` | browser scripts: signed-in state (a token, one file per server port) |

## Reference volume

Every V6 measurement runs on the same big data set, held in a named Docker volume (the template) and copied
into one working volume per bench before each series. A copy is proved equal to its template by
`verify` until a server opens it. Media folders and plugin folders are machine paths: they live in
`bench/.work/bench-local.json` (or the file `JUX_BENCH_LOCAL` names), never in the repository:

```json
{ "mediaMounts": ["E:/Films/Films:/media/films1"], "pluginDirs": { "native": "D:/bench/plugins-native" } }
```

| Command | Does |
|---|---|
| `node bench/volume-template.mjs create --from <config folder> --name <volume> --image jellyfin/jellyfin:10.11.11` | copies a Jellyfin `/config` (without `log/` and `plugins/`), opens it once with the image (forward migration: never open it with an older image), writes the stamp `.jux-template.json` (item counts, files, bytes, database sha256) |
| `node bench/volume-template.mjs copy --from <volume> --to <volume>` | copies a volume and verifies the copy |
| `node bench/volume-template.mjs verify <volume>` | compares a volume with its stamp, exit 1 on any difference |
| `node bench/volume-template.mjs shape <volume>` | `db-shape.mjs` on a copy of its database, exit 1 below 3 images per movie |
| `node bench/volume-template.mjs migrate --from <volume> --to <volume> --image jellyfin/jellyfin:12.3` | copies a volume, opens the copy with another image, exit 1 if the item counts change |

`node bench/db-shape.mjs <jellyfin.db>` reads a database copy: images, providers and user-data rows per item.

## Starting a bench

`node bench/bench-start.mjs --name <container> --port <port> --volume <working volume> --plugins <key|folder>
--profile std|lowend|2c8g|srvperso [--banner]` starts a disposable container on a working volume and waits
for `/health`, then for the warm-up line "Built the home screen" (the same fixed wait without Homepage).
The plugins folder (a `pluginDirs` key of the local file, or a folder) is copied without the Homepage
configuration file: every series starts on the defaults of the build under test. `--banner` adds the one
deviation of the "banner first" profile (a featured top-rated movie section above every row).

| Profile | Limits |
|---|---|
| `std` | none |
| `lowend` | 1 vCPU, 1 GiB, no swap |
| `2c8g` | 2 vCPU, 8 GiB |
| `srvperso` | cores 0-2, `SRV_CPUS` (default 1.0), 12 GiB: the production server's total throughput (calibrated 2026-10-25), a single thread stays 3.6 to 8 times faster; cite this limit with every result |

A disk IOPS limit (`--device-read-iops`) only applies to the WSL2 disk of Docker, never to a Windows bind
mount: no profile uses it, and an NTFS bind of `/config` is the "slow disk" variant.

## Series fingerprint

`JUX_BENCH_OUT` holds one series. Every `perf-*` script writes `fingerprint.json` there when it starts
(`bench-fingerprint.mjs`): item counts, images and providers per item type, the bench user's played,
liked, in-progress and favourite movies, Jellyfin version and image, plugins, repositories, md5 of the
Homepage DLL, `SessionTtlMinutes`, featured sections, deviations from the product defaults, CPU and memory
limits, storage of `/config` and the warm-up line. A second script in the same folder must find the same
fingerprint. At the end it writes `fingerprint-end.json` and fails when the Homepage DLL or the item counts
changed during the series (automatic plugin update, library scan). Changes applied on purpose are listed
through `JUX_BENCH_OVERRIDES` (items separated by `;`).

`node bench/perf-bench-summarize.mjs <series folder>` refuses a series with Homepage unless a native series
on the same data set and profile stands next to it (`--witness <folder>`, or the sibling `native-<suffix>`).
`node bench/perf-bench-summarize.mjs --compare <A> <B>` refuses two series whose fingerprints differ outside
the variable fields (build under test, clock, warm-up, container and volume names); `--allow
server.version,server.image` declares the fields a comparison changes on purpose.

`node bench/bench-archive.mjs <series folder> [--out <folder>]` zips a series (default
`bench/.work/archives`) and prints its sha256 as the registry proof `raw:<path>#sha256=<hex>`.

## Performance (main bench, `identifiants.txt`)

| Script | Measures |
|---|---|
| `perf-bench-endpoints.mjs --profile std --cycles 5 --warm 10` | cold and warm latency of every JellyUX GET endpoint, startup timings |
| `perf-bench-cycle.mjs --profile std --viewport desktop --cycles 5 --warm 5 [--banner]` | home page in a browser, cold then warm (uses `perf-bench-browser.mjs`); `mobile` is 390 x 844 at DPR 3 |
| `perf-bench-sections-interval.mjs --gaps 0,5,15,30,60 --reps 5` | `Sections` latency against the delay since the previous call |
| `perf-bench-tasks.mjs --runs 5` | duration of each JellyUX scheduled task |
| `perf-bench-plugin-toggle.mjs` | memory and startup with JellyUX disabled, then enabled again |
| `perf-server-sections-ttl.mjs` | `Sections` polled at a fixed step: cache expiry pattern |
| `perf-server-watchlist.mjs --reps 7` | `Watchlist/Ids` and `Watchlist/Items` against native queries |
| `perf-bench-summarize.mjs` | median and tail tables from the raw files: the p95 from 20 values, otherwise `max <value> (n)` |

`lowend.override.yml` limits the container to 1 CPU and 1 GiB for the low-end profile.

The browser scripts count a home row (`.homeSectionsContainer .verticalSection`, the same on native Jellyfin,
Homepage and KefinTweaks) as shown when it holds a real card or the loaded backdrop of the active banner
slide (`BENCH_SECTION_SEL`, `BENCH_CARD_SEL` override them). Before a series they check the selectors on
the target (`selectors-<viewport>.json`): a row with a card, no skeleton counted, and with `--banner` a
loaded banner first. Each home load records a timed chain from Resource Timing (`chain.*`: user known, home
and loadSections chunks, loadSections called, end of Sections, first row request, first row shown); the
loadSections call is estimated by the start of the first request it sends, no mark is placed in the product
(`BENCH_LOADSECTIONS_CHUNK` names the chunk of another Jellyfin Web bundle). `BENCH_KT_LOCAL=<folder>`
serves KefinTweaks from a local copy instead of jsDelivr.

Each browser summary prints the bytes the plugins inject (Homepage assets under `/JuxHomepage/`, KefinTweaks
files) and the common rows of `section-manifest.json` the series showed: two series compare the same rows
only when the same manifest rows are present on both sides.

## Load matrix

`node bench/perf-matrix.mjs --configs native,pile,full,hp --profiles lowend,2c8g,srvperso --remove-volumes`
runs each configuration (a `pluginDirs` key) on each server profile, on a fresh copy of the template per cell
(`jux-bench-mx-<config>`, replaced and removed by `--remove-volumes`), container `jux-bench-mx` on 8112. A
cell (`perf-matrix-cell.mjs`) measures cold pages after the warm-up, cold pages at `/health` (desktop) and
warm reloads, for the desktop, desktop-slow (CPU x4, low-end network) and phone (390 px DPR 3, CPU x4,
low-end network) browsers (`--browsers`, `--colds`, `--boot-colds`, `--warm-desktop`, `--warm-phone`). Raws
go to `<out>/<config>-<profile>` (one series each, `native-<profile>` is the witness of the others) and each
cell is archived to `<out>/archives`. `--image jellyfin/jellyfin:12.3 --template jux-bench-tpl-12` runs the
12.3 line. The matrix refuses to start next to another Jellyfin container.

`JUX_URL=<disposable bench> node bench/bench-history.mjs --played 500 --watchlist 100` gives the bench user
the history of the template (deterministic): 500 played, 100 liked, 10 in progress, 10 favourites.

## Reliability (disposable E2E bench on 8098)

Start it with `node e2e/setup.mjs up`; these scripts read its generated logins from `e2e/.work/env.json`.
They change the server, so never point them at a bench whose state matters.

| Script | Checks |
|---|---|
| `reliability-infinite-scroll.mjs [windowMs]` | requests sent by a finished library list |
| `reliability-admin-roundtrip.mjs` | an unchanged admin Save keeps every widget filter |
| `reliability-breadcrumb.mjs` | breadcrumb after detail, home, same detail |
| `reliability-diskfail.mjs markplayed\|history\|settings` | behaviour when a cache or settings write fails |
| `reliability-home-state.mjs <label>` | what a user sees on the home page |
| `reliability-api.mjs <METHOD> <route>` | one API call, for manual probes |
