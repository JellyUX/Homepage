// Disposable Jellyfin bench for the browser tests (TODO_V4.md rule G3): a Jellyfin container with the
// plugin built from this checkout, File Transformation, a tiny synthetic library, an admin and a
// user restricted to the movie library. Run from the repository root, after a Release build, or with
// the jprm archive a release publishes (--plugin-zip, what the CI installs):
//   node e2e/setup.mjs up [--image jellyfin/jellyfin:10.11.11] [--port 8098] [--plugin-zip <archive.zip>]
//   node e2e/setup.mjs base-url /jf     (sets a BaseUrl and restarts the server)
//   node e2e/setup.mjs load-config <plugin configuration .xml>   (replaces it and restarts the server)
//   node e2e/setup.mjs down
// State and generated credentials live in e2e/.work (never committed).

import { execFileSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { inflateRawSync } from 'node:zlib';

const CONTAINER = 'jux-e2e';
const WORK = path.resolve('e2e/.work');
const ENV_FILE = path.join(WORK, 'env.json');
const PLUGIN_DLL = path.resolve('src/Jellyfin.Plugin.JuxHomepage/bin/Release/net9.0/Jellyfin.Plugin.JuxHomepage.dll');

// File Transformation 3.0.0.0, the build matching each supported server version.
const FILE_TRANSFORMATION = {
    '10.11.11': {
        url: 'https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases/download/3.0.0.0/Release-10.11.11.zip',
        sha256: '53c6355627bef47fdea2e1237e0087fbdab065889137f7aacb5470899fed1937'
    },
    '10.11.10': {
        url: 'https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases/download/3.0.0.0/Release-10.11.10.zip',
        sha256: '243f047ae21329eaf8833d975ffffb6917cbf68997dd5e5af190237403e87f4a'
    },
    '10.11.9': {
        url: 'https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases/download/3.0.0.0/Release-10.11.9.zip',
        sha256: '8c05571c8d0761803eb1326d02c35c944191c8161cf373f2904c6977f1006295'
    }
};

// Twelve movies, more than one screen of cards at 1920 px (5 x 23.5vw) and at 390 px (3 x 45.5vw) (V7,
// PROC-01.b): the 4 dramas each have a backdrop, the 8 comedies 2 only, so a featured comedy section has
// too few backdrops for a banner and falls back to a row of cards wider than the screen.
const MOVIES = [
    ['Alder Creek', 2001, 'Drama', true], ['Birch Hollow', 2004, 'Drama', true],
    ['Cedar Point', 2009, 'Drama', true], ['Dogwood Lane', 2015, 'Drama', true],
    ['Ivy Court', 2003, 'Comedy', true], ['Juniper Hill', 2006, 'Comedy', true],
    ['Kapok Grove', 2008, 'Comedy', false], ['Larch Meadow', 2011, 'Comedy', false],
    ['Maple Ford', 2013, 'Comedy', false], ['Nutmeg Run', 2017, 'Comedy', false],
    ['Olive Wharf', 2019, 'Comedy', false], ['Pine Ridge', 2021, 'Comedy', false]
];
// Four of each: the recently added sections only show from MinItems = 4 (NativeWidgetDefaults).
const SHOWS = ['Elm Street Stories', 'Fir Valley', 'Ginkgo Bay', 'Hazel Ridge'];
const EPISODES = 2;
const AUTH = 'MediaBrowser Client="jux-e2e", Device="ci", DeviceId="jux-e2e", Version="1.0.0"';

function arg(name, fallback) {
    const i = process.argv.indexOf(name);
    return i > 0 ? process.argv[i + 1] : fallback;
}

function docker(...args) {
    return execFileSync('docker', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function api(env, method, route, { token, body } = {}) {
    const headers = { Authorization: token ? `${AUTH}, Token="${token}"` : AUTH };
    if (body !== undefined) {
        headers['Content-Type'] = 'application/json';
    }
    let res;
    for (let attempt = 0; ; attempt++) {
        try {
            res = await fetch(`${env.serverUrl}${route}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
        } catch (err) {
            // The startup page closes its connections when the real host takes over.
            if (attempt >= 120) {
                throw err;
            }
            await sleep(1000);
            continue;
        }
        // 503 while the server finishes starting, even once /System/Info/Public answers.
        if (res.status !== 503 || attempt >= 120) {
            break;
        }
        await sleep(1000);
    }
    if (!res.ok) {
        throw new Error(`${method} ${route}: HTTP ${res.status} ${(await res.text()).slice(0, 300)}`);
    }
    const text = await res.text();
    return text ? JSON.parse(text) : null;
}

async function waitForServer(serverUrl, timeoutMs = 180000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try {
            const res = await fetch(`${serverUrl}/System/Info/Public`);
            if (res.ok) {
                return await res.json();
            }
        } catch {
            // Not listening yet.
        }
        await sleep(1000);
    }
    throw new Error(`Jellyfin did not answer at ${serverUrl} within ${timeoutMs / 1000} s.`);
}

/** Extracts a zip archive with the standard library (stored and deflated entries only). */
function unzip(buffer, dest) {
    const eocd = buffer.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
    let offset = buffer.readUInt32LE(eocd + 16);
    const count = buffer.readUInt16LE(eocd + 10);
    for (let i = 0; i < count; i++) {
        const method = buffer.readUInt16LE(offset + 10);
        const size = buffer.readUInt32LE(offset + 20);
        const nameLength = buffer.readUInt16LE(offset + 28);
        const extraLength = buffer.readUInt16LE(offset + 30);
        const commentLength = buffer.readUInt16LE(offset + 32);
        const local = buffer.readUInt32LE(offset + 42);
        const name = buffer.toString('utf8', offset + 46, offset + 46 + nameLength);
        offset += 46 + nameLength + extraLength + commentLength;
        if (name.endsWith('/')) {
            continue;
        }
        const dataStart = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
        const data = buffer.subarray(dataStart, dataStart + size);
        const target = path.join(dest, path.basename(name));
        writeFileSync(target, method === 8 ? inflateRawSync(data) : data);
    }
}

async function installFileTransformation(serverVersion, pluginsDir) {
    const source = FILE_TRANSFORMATION[serverVersion];
    if (!source) {
        throw new Error(`No pinned File Transformation build for Jellyfin ${serverVersion}.`);
    }
    const res = await fetch(source.url);
    if (!res.ok) {
        throw new Error(`File Transformation download failed: HTTP ${res.status}`);
    }
    const zip = Buffer.from(await res.arrayBuffer());
    const digest = createHash('sha256').update(zip).digest('hex');
    if (digest !== source.sha256) {
        throw new Error(`File Transformation checksum mismatch: ${digest}`);
    }
    const dir = path.join(pluginsDir, 'File Transformation_3.0.0.0');
    mkdirSync(dir, { recursive: true });
    unzip(zip, dir);
}

function writeLibrary(image) {
    const media = path.join(WORK, 'media');
    mkdirSync(media, { recursive: true });
    docker('run', '--rm', '--entrypoint', '/usr/lib/jellyfin-ffmpeg/ffmpeg', '-v', `${media}:/out`, image,
        '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc=duration=2:size=320x180:rate=10',
        '-f', 'lavfi', '-i', 'sine=duration=2', '-c:v', 'libx264', '-c:a', 'aac', '-shortest', '/out/seed.mkv');
    const seed = path.join(media, 'seed.mkv');
    // A local backdrop for the movies flagged in MOVIES (read by Jellyfin's local image provider, no fetcher
    // needed): a featured section shows only items with a backdrop (KTW-01, KTW-02); the shows keep none.
    docker('run', '--rm', '--entrypoint', '/usr/lib/jellyfin-ffmpeg/ffmpeg', '-v', `${media}:/out`, image,
        '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=size=1280x720', '-frames:v', '1', '/out/backdrop.jpg');
    const backdrop = path.join(media, 'backdrop.jpg');

    for (const [title, year, genre, hasBackdrop] of MOVIES) {
        const dir = path.join(media, 'movies', `${title} (${year})`);
        mkdirSync(dir, { recursive: true });
        copyFileSync(seed, path.join(dir, `${title} (${year}).mkv`));
        if (hasBackdrop) {
            copyFileSync(backdrop, path.join(dir, 'backdrop.jpg'));
        }
        writeFileSync(path.join(dir, 'movie.nfo'),
            `<movie><title>${title}</title><year>${year}</year><genre>${genre}</genre><rating>7</rating></movie>`);
    }

    for (const title of SHOWS) {
        const show = path.join(media, 'shows', title);
        mkdirSync(path.join(show, 'Season 01'), { recursive: true });
        writeFileSync(path.join(show, 'tvshow.nfo'), `<tvshow><title>${title}</title><year>2020</year><genre>Drama</genre></tvshow>`);
        for (let e = 1; e <= EPISODES; e++) {
            copyFileSync(seed, path.join(show, 'Season 01', `${title} S01E0${e}.mkv`));
        }
    }
}

// Local files only: no internet metadata, no image extraction, so the scan is fast and stable.
function libraryOptions() {
    const offline = (type) => ({ Type: type, MetadataFetchers: [], ImageFetchers: [] });
    return {
        LibraryOptions: {
            EnableRealtimeMonitor: false,
            EnableChapterImageExtraction: false,
            ExtractChapterImagesDuringLibraryScan: false,
            EnableTrickplayImageExtraction: false,
            SaveLocalMetadata: false,
            MetadataSavers: [],
            TypeOptions: ['Movie', 'Series', 'Season', 'Episode'].map(offline)
        }
    };
}

async function up() {
    const image = arg('--image', 'jellyfin/jellyfin:10.11.11');
    const port = arg('--port', '8098');
    const serverVersion = image.split(':')[1];
    const pluginZip = arg('--plugin-zip', '');
    if (pluginZip && !existsSync(pluginZip)) {
        throw new Error(`Plugin archive not found: ${pluginZip}.`);
    }
    if (!pluginZip && !existsSync(PLUGIN_DLL)) {
        throw new Error(`Build the plugin first (dotnet build -c Release): ${PLUGIN_DLL} is missing.`);
    }

    down();
    const pluginsDir = path.join(WORK, 'config', 'plugins');
    const juxDir = path.join(pluginsDir, 'JellyUX Homepage');
    mkdirSync(juxDir, { recursive: true });
    if (pluginZip) {
        unzip(readFileSync(pluginZip), juxDir);
    } else {
        copyFileSync(PLUGIN_DLL, path.join(juxDir, path.basename(PLUGIN_DLL)));
    }
    await installFileTransformation(serverVersion, pluginsDir);
    writeLibrary(image);

    docker('run', '-d', '--name', CONTAINER, '-p', `${port}:8096`,
        '-v', `${path.join(WORK, 'config')}:/config`, '-v', `${path.join(WORK, 'media')}:/media:ro`, image);

    const env = {
        origin: `http://localhost:${port}`,
        serverUrl: `http://localhost:${port}`,
        image,
        admin: { name: 'admin', password: randomBytes(12).toString('hex') },
        restricted: { name: 'restricted', password: randomBytes(12).toString('hex') },
        movies: MOVIES.map(([t]) => t),
        moviesWithBackdrop: MOVIES.filter((m) => m[3]).map(([t]) => t),
        shows: SHOWS
    };
    await waitForServer(env.serverUrl);

    await api(env, 'POST', '/Startup/Configuration', { body: { UICulture: 'en-US', MetadataCountryCode: 'US', PreferredMetadataLanguage: 'en' } });
    await api(env, 'GET', '/Startup/User');
    await api(env, 'POST', '/Startup/User', { body: { Name: env.admin.name, Password: env.admin.password } });
    await api(env, 'POST', '/Startup/RemoteAccess', { body: { EnableRemoteAccess: true, EnableAutomaticPortMapping: false } });
    await api(env, 'POST', '/Startup/Complete');

    const auth = await api(env, 'POST', '/Users/AuthenticateByName', { body: { Username: env.admin.name, Pw: env.admin.password } });
    const token = auth.AccessToken;
    env.admin.id = auth.User.Id;

    const folder = (name, type, dir) =>
        api(env, 'POST', `/Library/VirtualFolders?name=${name}&collectionType=${type}&paths=${encodeURIComponent(dir)}&refreshLibrary=false`,
            { token, body: libraryOptions() });
    await folder('Movies', 'movies', '/media/movies');
    await folder('Shows', 'tvshows', '/media/shows');
    await api(env, 'POST', '/Library/Refresh', { token });

    const expected = MOVIES.length + SHOWS.length * EPISODES;
    const items = async () => (await api(env, 'GET', `/Items?recursive=true&includeItemTypes=Movie,Episode&userId=${env.admin.id}`, { token })).Items;
    const start = Date.now();
    while ((await items()).length < expected) {
        if (Date.now() - start > 180000) {
            throw new Error('The library scan did not finish within 180 s.');
        }
        await sleep(2000);
    }

    const folders = await api(env, 'GET', '/Library/VirtualFolders', { token });
    const moviesLibrary = folders.find((f) => f.Name === 'Movies').ItemId;
    const user = await api(env, 'POST', '/Users/New', { token, body: { Name: env.restricted.name, Password: env.restricted.password } });
    env.restricted.id = user.Id;
    await api(env, 'POST', `/Users/${user.Id}/Policy`,
        { token, body: { ...user.Policy, EnableAllFolders: false, EnabledFolders: [moviesLibrary] } });

    // Watch state, so Continue Watching, Next Up and the Watchlist (liked items) have something to show.
    const all = await items();
    const firstMovie = all.find((i) => i.Name === MOVIES[0][0]);
    const likedMovie = all.find((i) => i.Name === MOVIES[1][0]);
    const firstEpisode = all.find((i) => i.Type === 'Episode' && i.SeriesName === SHOWS[0] && i.IndexNumber === 1);
    await api(env, 'POST', `/UserItems/${firstMovie.Id}/UserData?userId=${env.admin.id}`,
        { token, body: { PlaybackPositionTicks: 5_000_000, LastPlayedDate: new Date().toISOString() } });
    await api(env, 'POST', `/UserPlayedItems/${firstEpisode.Id}?userId=${env.admin.id}`, { token });
    await api(env, 'POST', `/UserItems/${likedMovie.Id}/UserData?userId=${env.admin.id}`, { token, body: { Likes: true } });

    writeFileSync(ENV_FILE, JSON.stringify(env, null, 2));
    console.log(`Jellyfin bench ready at ${env.serverUrl} (${image}, ${all.length} items).`);
}

async function setBaseUrl() {
    const baseUrl = process.argv[3] ?? '';
    // Guards against a shell rewriting the argument (Git Bash turns "/jf" into a Windows path).
    if (!/^\/[\w-]+$/.test(baseUrl)) {
        throw new Error(`BaseUrl must look like "/jf", got "${baseUrl}".`);
    }
    const env = JSON.parse(readFileSync(ENV_FILE, 'utf8'));
    const auth = await api(env, 'POST', '/Users/AuthenticateByName', { body: { Username: env.admin.name, Pw: env.admin.password } });
    const network = await api(env, 'GET', '/System/Configuration/network', { token: auth.AccessToken });
    await api(env, 'POST', '/System/Configuration/network', { token: auth.AccessToken, body: { ...network, BaseUrl: baseUrl } });
    docker('restart', CONTAINER);
    env.serverUrl = `${env.origin}${baseUrl}`;
    await waitForServer(env.serverUrl);
    writeFileSync(ENV_FILE, JSON.stringify(env, null, 2));
    console.log(`BaseUrl set to "${baseUrl}", server at ${env.serverUrl}.`);
}

/**
 * Replaces the plugin configuration file with one saved by an earlier release and restarts the server,
 * so the plugin migrates it on load as it would after an upgrade.
 */
async function loadConfig() {
    const source = process.argv[3] ?? '';
    if (!source.endsWith('.xml') || !existsSync(source)) {
        throw new Error(`load-config needs an existing .xml file, got "${source}".`);
    }
    const env = JSON.parse(readFileSync(ENV_FILE, 'utf8'));
    docker('stop', CONTAINER);
    // Through Docker, not the bind mount: the server runs as root and owns that file, which a non-root
    // CI runner could not overwrite from the host.
    docker('cp', source, `${CONTAINER}:/config/plugins/configurations/Jellyfin.Plugin.JuxHomepage.xml`);
    docker('start', CONTAINER);
    await waitForServer(env.serverUrl);
    // /System/Info/Public answers before the server takes requests: wait for a sign-in to go through.
    await api(env, 'POST', '/Users/AuthenticateByName', { body: { Username: env.admin.name, Pw: env.admin.password } });
    console.log(`Plugin configuration replaced by ${source}, server restarted at ${env.serverUrl}.`);
}

function down() {
    try {
        docker('rm', '-f', CONTAINER);
    } catch {
        // No container to remove.
    }
    rmSync(WORK, { recursive: true, force: true });
}

const command = process.argv[2];
if (command === 'up') {
    await up();
} else if (command === 'base-url') {
    await setBaseUrl();
} else if (command === 'load-config') {
    await loadConfig();
} else if (command === 'down') {
    down();
} else {
    console.error('Usage: node e2e/setup.mjs up|base-url <path>|load-config <xml>|down');
    process.exitCode = 2;
}
