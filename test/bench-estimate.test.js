import { describe, it, expect } from 'vitest';
import { cellOptions, cellSteps, estimateCellS, etaMs, timeoutProblem, TIMEOUT_MARGIN } from '../bench/bench-estimate.mjs';

const argOf = (values) => (n, d) => (n in values ? String(values[n]) : d);
// The cell options of the phase 12 A/B run (2026-11-02).
const P12 = cellOptions(argOf({ colds: 5, 'boot-colds': 0, 'warm-desktop': 20, 'warm-phone': 20, 'min-uptime-s': 120 }));

describe('bench duration estimate (2026-11-02 audit of the announced durations)', () => {
    it('gives back the measured cells of the phase 12 run within 3 %: native 56 min, Homepage 70 and 71 min', () => {
        expect(Math.abs(estimateCellS('native', P12) / 60 - 56) / 56).toBeLessThan(0.03);
        expect(Math.abs(estimateCellS('hpB', P12) / 60 - 70.5) / 70.5).toBeLessThan(0.03);
    });

    it('lists the steps in the cell order: colds per browser, boot colds, then one warm phase per browser', () => {
        const steps = cellSteps('hp', cellOptions(argOf({ colds: 2, 'boot-colds': 1, browsers: 'desktop,phone', 'warm-persisted': 5 })));
        expect(steps.map((s) => s.label)).toEqual([
            'desktop cold 1', 'phone cold 1', 'desktop cold 2', 'phone cold 2', 'desktop boot 1',
            'desktop warm', 'phone warm', 'desktop warm-persisted', 'phone warm-persisted'
        ]);
    });

    it('shortens the cold slots when the minimum uptime is below the measured 120 s', () => {
        const short = cellSteps('native', cellOptions(argOf({ colds: 1, browsers: 'desktop', 'min-uptime-s': 0 })))[0].s;
        const long = cellSteps('native', cellOptions(argOf({ colds: 1, browsers: 'desktop', 'min-uptime-s': 120 })))[0].s;
        expect(long - short).toBe(60);
    });

    it('refuses a caller timeout below 1.3 x the estimate, as the 3600 s per cell of phase 12 was', () => {
        const estimateS = estimateCellS('hpA', P12);
        expect(timeoutProblem(3600, estimateS)).toMatch(/below 1\.3 x the estimate/);
        expect(timeoutProblem(Math.ceil(estimateS * TIMEOUT_MARGIN), estimateS)).toBeNull();
    });

    it('scales the remaining time by the pace of the finished steps', () => {
        // 600 s estimated done in 900 s: the 1 000 s left take 1 500 s.
        expect(etaMs(900000, 0, 600, 1000)).toBe(900000 + 1500000);
        expect(etaMs(5000, 5000, 0, 100)).toBe(5000 + 100000);
    });
});
