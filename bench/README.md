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
| `perf-bench-summarize.mjs` | median and p95 tables from the raw files |

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
