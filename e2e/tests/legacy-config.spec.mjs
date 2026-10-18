import { readFileSync } from 'node:fs';
import { test, expect } from '@playwright/test';
import { env, login, loadedSections, apiGet, watchJuxErrors } from '../helpers.mjs';

// Runs after `node e2e/setup.mjs load-config e2e/fixtures/config-2.13.0.xml`: the file the 2.13.0 release
// wrote (schema 10) on the bench, with next-up turned off and a Drama genre row added through its API.

const PLUGIN_ID = '3adf1f1f-1541-4e47-b9e3-34d2d2968af6';

/** The rows of the fixture, in file order. */
function fixtureRows() {
    const xml = readFileSync(new URL('../fixtures/config-2.13.0.xml', import.meta.url), 'utf8');
    return [...xml.matchAll(/<WidgetConfig>([\s\S]*?)<\/WidgetConfig>/g)].map(([, row]) => ({
        WidgetType: /<WidgetType>([^<]+)</.exec(row)[1],
        Enabled: /<Enabled>(\w+)</.exec(row)[1] === 'true',
        Order: Number(/<Order>(\d+)</.exec(row)[1]),
        Value: /<Key>value<\/Key>\s*<Value>([^<]*)</.exec(row)?.[1] ?? null
    }));
}

test('a configuration saved by 2.13.0 loads as is, migrated to the current schema (PROC-03.c)', async ({ page }) => {
    const config = await apiGet(env.admin, `/Plugins/${PLUGIN_ID}/Configuration`);
    const value = (w) => w.ExtraParams.find((p) => p.Key === 'value')?.Value ?? null;
    const loaded = config.Widgets.map((w) => ({ WidgetType: w.WidgetType, Enabled: w.Enabled, Order: w.Order, Value: value(w) }));

    expect(config.SchemaVersion).toBeGreaterThan(10);
    for (const row of fixtureRows()) {
        expect(loaded).toContainEqual(row);
    }
    // Rows a later schema tier adds arrive turned off, so an upgrade changes no home page.
    const added = loaded.filter((w) => !fixtureRows().some((r) => r.WidgetType === w.WidgetType && r.Order === w.Order && r.Value === w.Value));
    expect(added.filter((w) => w.Enabled)).toEqual([]);

    const errors = watchJuxErrors(page);
    await login(page, env.admin);
    const sections = await loadedSections(page);
    expect(sections).toContain('jux-admin-genre');
    expect(sections).not.toContain('jux-native-next-up');
    expect(errors).toEqual([]);
});
