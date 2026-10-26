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

## Performance (main bench, `identifiants.txt`)

| Script | Measures |
|---|---|
| `perf-bench-endpoints.mjs --profile std --cycles 5 --warm 10` | cold and warm latency of every JellyUX GET endpoint, startup timings |
| `perf-bench-cycle.mjs --profile std --viewport desktop --cycles 5 --warm 5` | home page in a browser, cold then warm (uses `perf-bench-browser.mjs`) |
| `perf-bench-sections-interval.mjs --gaps 0,5,15,30,60 --reps 5` | `Sections` latency against the delay since the previous call |
| `perf-bench-tasks.mjs --runs 5` | duration of each JellyUX scheduled task |
| `perf-bench-plugin-toggle.mjs` | memory and startup with JellyUX disabled, then enabled again |
| `perf-server-sections-ttl.mjs` | `Sections` polled at a fixed step: cache expiry pattern |
| `perf-server-watchlist.mjs --reps 7` | `Watchlist/Ids` and `Watchlist/Items` against native queries |
| `perf-bench-summarize.mjs` | median and tail tables from the raw files: the p95 from 20 values, otherwise `max <value> (n)` |

`lowend.override.yml` limits the container to 1 CPU and 1 GiB for the low-end profile.

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
