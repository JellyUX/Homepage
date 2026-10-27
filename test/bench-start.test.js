import { describe, it, expect, afterEach } from 'vitest';
import { profileLimits, remainingUptimeMs } from '../bench/bench-docker.mjs';

describe('bench cold page age (PERF-12 cold: server started more than 2 min ago)', () => {
    it('waits for what the server still lacks of the minimum uptime, and never a negative time', () => {
        expect(remainingUptimeMs(1000, 120, 1000 + 45000)).toBe(75000);
        expect(remainingUptimeMs(1000, 120, 1000 + 130000)).toBe(0);
        expect(remainingUptimeMs(1000, 0, 1000)).toBe(0);
    });
});

describe('bench server profiles (V5, PROC-11)', () => {
    afterEach(() => { delete process.env.SRV_CPUS; });

    it('pins the personal server profile to 3 cores, SRV_CPUS of them (1.0 by default) and 12 GiB', () => {
        expect(profileLimits('srvperso')).toEqual(['--cpuset-cpus', '0-2', '--cpus', '1.0', '--memory', '12g', '--memory-swap', '12g']);
        process.env.SRV_CPUS = '1.2';
        expect(profileLimits('srvperso')).toContain('1.2');
    });

    it('limits the low-end profile to 1 vCPU and 1 GiB without swap, and leaves std unlimited', () => {
        expect(profileLimits('lowend')).toEqual(['--cpus', '1', '--memory', '1g', '--memory-swap', '1g']);
        expect(profileLimits('std')).toEqual([]);
    });

    it('refuses an unknown profile', () => {
        expect(() => profileLimits('fast')).toThrow(/unknown profile fast/);
    });
});
