// Shape of a Jellyfin 10.11 library database: images, providers and user-data rows per item, and how many
// rows the single-query EF Core includes multiply each item into. Counts only, no timing (MB-06, PROC-01.a).
// Ported from the audit V5 db-shape.py. Run it on a COPY of the database, never on the file of a running
// server (bench/volume-template.mjs shape does the copy):
//   node bench/db-shape.mjs <jellyfin.db> [--min-movie-images 3]
// Mean row multiplier of the include sets JellyUX uses:
//   standard = Images x UserData                              (WidgetDtoOptions.Standard as query DtoOptions)
//   default  = Images x UserData x Providers x LockedFields   (new InternalItemsQuery { ItemIds }: all fields)
import { fileURLToPath } from 'node:url';

const TYPES = {
    Movie: 'MediaBrowser.Controller.Entities.Movies.Movie',
    Series: 'MediaBrowser.Controller.Entities.TV.Series',
    Episode: 'MediaBrowser.Controller.Entities.TV.Episode'
};

/**
 * @param {string} file path of a database copy
 * @returns {Promise<{users: number, types: Record<string, {n: number, imagesPerItem: number, providersPerItem: number, userDataPerItem: number, rowsStandard: number, rowsDefault: number, withAtLeast3Images: number}>}>}
 */
export async function dbShape(file) {
    const { DatabaseSync } = await import('node:sqlite');
    const db = new DatabaseSync(file, { readOnly: true });
    try {
        const tables = new Set(db.prepare("select name from sqlite_master where type = 'table'").all().map((r) => r.name));
        const missing = ['BaseItems', 'BaseItemImageInfos', 'BaseItemProviders', 'UserData'].filter((t) => !tables.has(t));
        if (missing.length > 0) { throw new Error(`missing tables: ${missing.join(', ')}`); }
        const countBy = (table) => new Map(db.prepare(`select ItemId as id, count(*) as n from ${table} group by ItemId`).all().map((r) => [r.id, r.n]));
        const images = countBy('BaseItemImageInfos');
        const providers = countBy('BaseItemProviders');
        const userData = countBy('UserData');
        const locked = tables.has('BaseItemMetadataFields') ? countBy('BaseItemMetadataFields') : new Map();
        const users = tables.has('Users') ? db.prepare('select count(*) as n from Users').get().n : 0;
        const types = {};
        for (const [name, type] of Object.entries(TYPES)) {
            const ids = db.prepare('select Id as id from BaseItems where Type = ?').all(type).map((r) => r.id);
            if (ids.length === 0) { continue; }
            const at = (m, id) => m.get(id) || 0;
            const one = (m, id) => Math.max(1, at(m, id));
            const sum = (f) => ids.reduce((a, id) => a + f(id), 0);
            const n = ids.length;
            types[name] = {
                n,
                imagesPerItem: sum((id) => at(images, id)) / n,
                providersPerItem: sum((id) => at(providers, id)) / n,
                userDataPerItem: sum((id) => at(userData, id)) / n,
                rowsStandard: sum((id) => one(images, id) * one(userData, id)) / n,
                rowsDefault: sum((id) => one(images, id) * one(userData, id) * one(providers, id) * one(locked, id)) / n,
                withAtLeast3Images: ids.filter((id) => at(images, id) >= 3).length
            };
        }
        return { users, types };
    } finally {
        db.close();
    }
}

/** PROC-01.a: the reference set is valid only with at least `min` images per movie on average. */
export function checkMovieImages(shape, min) {
    const movie = shape.types.Movie;
    if (!movie) { return `no movie in the database`; }
    return movie.imagesPerItem >= min ? null : `${movie.imagesPerItem.toFixed(2)} images per movie, below ${min}`;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const file = process.argv[2];
    const i = process.argv.indexOf('--min-movie-images');
    const min = i > 0 ? Number(process.argv[i + 1]) : 3;
    const shape = await dbShape(file);
    console.log(JSON.stringify(shape, null, 2));
    const problem = checkMovieImages(shape, min);
    if (problem) { console.error(`db-shape: ${problem}`); process.exit(1); }
}
