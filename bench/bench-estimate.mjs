// Duration estimate of a load matrix (bench/perf-matrix.mjs) and of its cells, from unit costs measured on the
// big data set (srvperso, jellyfin/jellyfin:10.11.11, --min-uptime-s 120; phase 12 A/B run of 2026-11-02:
// native cell 56 min, Homepage cells 70 and 71 min). A cold slot is the restart, the wait for the warm-up line or
// the minimum uptime, then the page: about the same whatever the page speed. A warm phase loads one uncounted page
// first. Recalibrate COSTS from the durations.json a matrix writes next to its cells.

export const COSTS = {
    // The native configuration (no plugin) and any configuration with plugins (Homepage, the production stack).
    native: {
        setupS: 120, endS: 16, readyS: 60,
        coldSlotS: { desktop: 146, 'desktop-slow': 146, phone: 145 },
        warmPageS: { desktop: 10.4, 'desktop-slow': 19.9, phone: 20 },
        bootColdS: 25, persistedPageS: 10.6
    },
    plugins: {
        setupS: 243, endS: 16, readyS: 95,
        coldSlotS: { desktop: 160, 'desktop-slow': 160, phone: 160 },
        warmPageS: { desktop: 18.5, 'desktop-slow': 29.1, phone: 27.9 },
        bootColdS: 27, persistedPageS: 11.7
    }
};

// The uptime the cold slots above were measured with.
const MEASURED_UPTIME_S = 120;

/** The margin a caller's timeout must keep over the estimate (memory rule of 2026-11-02). */
export const TIMEOUT_MARGIN = 1.3;

export function costsFor(config) { return config === 'native' ? COSTS.native : COSTS.plugins; }

/** The steps of one cell, in the order perf-matrix-cell.mjs runs them, each with its estimated seconds. */
export function cellSteps(config, o) {
    const c = costsFor(config);
    const uptimeShiftS = Math.max(o.minUptimeS, c.readyS) - Math.max(MEASURED_UPTIME_S, c.readyS);
    const steps = [];
    for (let i = 1; i <= o.colds; i++) {
        for (const b of o.browsers) { steps.push({ label: `${b} cold ${i}`, s: Math.max(30, c.coldSlotS[b] + uptimeShiftS) }); }
    }
    if (o.browsers.includes('desktop')) {
        for (let i = 1; i <= o.bootColds; i++) { steps.push({ label: `desktop boot ${i}`, s: c.bootColdS }); }
    }
    for (const b of o.browsers) { steps.push({ label: `${b} warm`, s: (o.warm[b] + 1) * c.warmPageS[b] }); }
    for (const b of o.browsers.filter((x) => x !== 'desktop-slow' && o.warmPersisted > 0)) {
        steps.push({ label: `${b} warm-persisted`, s: (o.warmPersisted + 1) * c.persistedPageS });
    }
    return steps;
}

/** Estimated seconds of one cell: setup (volume copy, start, warm-up), its steps, archive and teardown. */
export function estimateCellS(config, o) {
    const c = costsFor(config);
    return c.setupS + cellSteps(config, o).reduce((sum, st) => sum + st.s, 0) + c.endS;
}

/** The cell options of perf-matrix.mjs / perf-matrix-cell.mjs, with their defaults. */
export function cellOptions(arg) {
    const warmDesktop = Number(arg('warm-desktop', 20));
    return {
        browsers: arg('browsers', 'desktop,desktop-slow,phone').split(','),
        colds: Number(arg('colds', 3)),
        bootColds: Number(arg('boot-colds', 3)),
        warm: { desktop: warmDesktop, 'desktop-slow': warmDesktop, phone: Number(arg('warm-phone', 10)) },
        warmPersisted: Number(arg('warm-persisted', 0)),
        minUptimeS: Number(arg('min-uptime-s', 0))
    };
}

export function clock(ms) { return new Date(ms).toTimeString().slice(0, 5); }

/** Remaining time of a run, scaled by how fast the finished steps went against their estimate. */
export function etaMs(nowMs, startMs, doneEstS, leftEstS) {
    const ratio = doneEstS > 0 ? (nowMs - startMs) / 1000 / doneEstS : 1;
    return nowMs + leftEstS * Math.max(ratio, 0.5) * 1000;
}

/** A refusal message when a caller's timeout keeps less than TIMEOUT_MARGIN over the estimate, otherwise null. */
export function timeoutProblem(timeoutS, estimateS) {
    const needS = Math.ceil(estimateS * TIMEOUT_MARGIN);
    return timeoutS < needS ? `--timeout-s ${timeoutS} is below ${TIMEOUT_MARGIN} x the estimate (${needS} s): raise it, or pass --force` : null;
}
