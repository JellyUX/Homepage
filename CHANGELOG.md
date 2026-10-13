## [2.12.1](https://github.com/JellyUX/Homepage/compare/v2.12.0...v2.12.1) (2026-10-13)

### Bug Fixes

* add the JellyUX tabs in the same frame as the native tabs ([b1fe806](https://github.com/JellyUX/Homepage/commit/b1fe8067e56fb174affa2243fe71e499e88931ff))
* align the tab titles, controls and tiles on the native page gutters ([8a766e2](https://github.com/JellyUX/Homepage/commit/8a766e220df4313b1dab55e33bd1d32fc282a92b))
* keep the JellyUX outlines visible in the light theme ([703345a](https://github.com/JellyUX/Homepage/commit/703345adcd967eed57f2ccc9ee95a3abb4d220e5))
* remove the snow overlay from the Christmas seasonal section ([437a8f0](https://github.com/JellyUX/Homepage/commit/437a8f01ad16e3c74c0879c4487fc6f38d42ba9a))
* shorten the Statistics tab on narrow screens and lay the stat tiles on a grid ([0406696](https://github.com/JellyUX/Homepage/commit/04066965575435b4f73f5e4eaf6213dd0c516f94))
* show a visible keyboard focus ring and name the progress bars ([fbbb0a9](https://github.com/JellyUX/Homepage/commit/fbbb0a98a73566ffa3adf0c9c78ccb7e566b0368))
* show one radio per option in the sort and filter dialogs ([852b9c5](https://github.com/JellyUX/Homepage/commit/852b9c50824f08ece9a4c978479c80879862138c))
* style Mark as watched as a pill aligned across the Series Progress row ([f548a0b](https://github.com/JellyUX/Homepage/commit/f548a0b524501f4d4f9d43ea267bc6b67510a2db))
* toggle a History favorite back and forth without reloading the tab ([6d59fb3](https://github.com/JellyUX/Homepage/commit/6d59fb392e27073c7e0d987b295f5a4007383aa6))

## [2.12.0](https://github.com/JellyUX/Homepage/compare/v2.11.1...v2.12.0) (2026-10-12)

### Features

* add a Preview button to every admin section card ([9742330](https://github.com/JellyUX/Homepage/commit/9742330ff95580aa48e3ed043f4c467166c76d8a))
* list the configuration page in the dashboard plugin menu ([b8597c3](https://github.com/JellyUX/Homepage/commit/b8597c3c90b1765c926661d1712878b7bf6c4c3a))
* move an admin section to any position by number or drag and drop ([9374141](https://github.com/JellyUX/Homepage/commit/93741413a8301bf6762e70786813156e20fbc4e2))
* preview an unsaved home section from the admin page ([c63bc9e](https://github.com/JellyUX/Homepage/commit/c63bc9e1c6b9185e763733d95c778713223f6567))

### Bug Fixes

* align the admin card controls and describe every way to reorder ([10b1e58](https://github.com/JellyUX/Homepage/commit/10b1e583b653f144ac6bd7a749e0b91e00a58f60))
* apply a typed section position on Enter instead of saving the page ([f5d079e](https://github.com/JellyUX/Homepage/commit/f5d079edcd611c81528660853f582720898c3400))
* classify the seasonal widget as Admin and choose admin editors by widget type ([a49514c](https://github.com/JellyUX/Homepage/commit/a49514cdbf944d17dcdd005d2ca5965bd590eb31))
* keep Save in view and fold the admin section cards into a summary line ([8b18957](https://github.com/JellyUX/Homepage/commit/8b18957fd1544fe51e9dc848a312961587400b60))
* mask the TMDb API key on the configuration page ([cc4c550](https://github.com/JellyUX/Homepage/commit/cc4c550495b7177ae9c0f0812dd9a3136f8b230c))
* move a section only once when its typed position is applied ([9f82eff](https://github.com/JellyUX/Homepage/commit/9f82eff3bce7b0ef88932e889a288f06f13e5ec9))
* serve the configuration page stylesheet without a one-hour cache ([fa2ec6e](https://github.com/JellyUX/Homepage/commit/fa2ec6ef683639de533a61c5089b7a5adefecf29))
* show the Now Playing region in full and keep the TMDb rows inside narrow screens ([65ff3c9](https://github.com/JellyUX/Homepage/commit/65ff3c99fea56feaee4ad4b4c2e670f408f28965))

## [2.11.1](https://github.com/JellyUX/Homepage/compare/v2.11.0...v2.11.1) (2026-10-12)

### Bug Fixes

* bound the paging and language parameters and honor MaxItems ([e65aa9e](https://github.com/JellyUX/Homepage/commit/e65aa9eef1c5af4e9d3527c89e9830909616f3a9))
* escape apostrophes and load card images through one shared core ([24c1ab3](https://github.com/JellyUX/Homepage/commit/24c1ab3a845aee53479a6f9e33ab0b4770970662))
* keep JellyUX failures out of the Jellyfin operations and tab answers ([8bebc60](https://github.com/JellyUX/Homepage/commit/8bebc602ca278677b9a5a193c3751593801eeba4))
* load the watchlist ids of each signed-in user, even after a slow sign-in ([a4bb547](https://github.com/JellyUX/Homepage/commit/a4bb5479ea8094e533015f29df6f9adcf80c1c1f))
* read the day from one time source and validate seasonal MM-dd dates ([49a2202](https://github.com/JellyUX/Homepage/commit/49a2202f3f0077bb8695625a172f92a4ad93a315))
* send each detail-item request once across detail-page modules ([050271c](https://github.com/JellyUX/Homepage/commit/050271c40ae47a92cf21a9c5dfc98a5d5b34b07f))
* share the detail-item request with the detail page watchlist button ([60577e3](https://github.com/JellyUX/Homepage/commit/60577e3e55a372eab654d77f424396a71f8a7fa1))
* show an error with a retry when a tab or a home section fails ([9b8a19a](https://github.com/JellyUX/Homepage/commit/9b8a19a882494c097696552c3ccb5e7a62d48434))
* show grey placeholders at once and drop the meta request from the home page ([eeed8f7](https://github.com/JellyUX/Homepage/commit/eeed8f78cbd8c88cb295c78abc249220097e0bd5))

## [2.11.0](https://github.com/JellyUX/Homepage/compare/v2.10.1...v2.11.0) (2026-10-12)

### Features

* warn administrators when Custom Tabs is installed alongside JellyUX ([f127165](https://github.com/JellyUX/Homepage/commit/f1271658f4589901c16f6bd74ba7fa38107cff1b))

### Bug Fixes

* number JellyUX tabs after their real pane position in the home view ([8380064](https://github.com/JellyUX/Homepage/commit/83800646fe5a474e2073e8896448f92f40a53712))
* read the native home sections from the native Jellyfin routes ([a293fd3](https://github.com/JellyUX/Homepage/commit/a293fd3dfc056815709a5938eddbc25944a6caf7))
* reload once when the browser serves a cached native home chunk ([bb39e6a](https://github.com/JellyUX/Homepage/commit/bb39e6a2f8cc4cd77e3d882c8e2341419275e9fa))

## [2.10.1](https://github.com/JellyUX/Homepage/compare/v2.10.0...v2.10.1) (2026-10-12)

### Bug Fixes

* build the plugin against the minimum supported Jellyfin ([bb3d47a](https://github.com/JellyUX/Homepage/commit/bb3d47a23b3a4344bf9b35786d3227a4cf4eef21))
* drop the CSS order set on home sections ([8cf0d7e](https://github.com/JellyUX/Homepage/commit/8cf0d7e8240b9a80842388806a80635998f567f3))
* fall back to native rendering when a patched chunk token is missing ([c6df508](https://github.com/JellyUX/Homepage/commit/c6df50818c6cf6ad9d7562badbcab883a99aee7a))

## [2.10.0](https://github.com/JellyUX/Homepage/compare/v2.9.2...v2.10.0) (2026-10-11)

### Features

* add a switch for each JellyUX client module ([acb9bd0](https://github.com/JellyUX/Homepage/commit/acb9bd0627d7c5e562a169150b59f9e4fb2b6965))

### Bug Fixes

* cap the progress bar at 50 episode chunks ([72cc730](https://github.com/JellyUX/Homepage/commit/72cc73031f3c31c740c4ebee19b25aa21fe2cbcf))
* inject only the scripts of enabled modules ([3472b5a](https://github.com/JellyUX/Homepage/commit/3472b5ace6a352b40c16062bd268e9ff134b26e9))
* load tab card images only when they come into view ([ebe8f03](https://github.com/JellyUX/Homepage/commit/ebe8f031668ee1e899d0d3527ae60b67c2fbbc41))
* serve client assets as immutable under a content hash ([759aae1](https://github.com/JellyUX/Homepage/commit/759aae1fb272d3adda3fd6684aba6325228a0c1b))
* stop detail-page modules from refetching on every DOM change ([2ce7624](https://github.com/JellyUX/Homepage/commit/2ce7624b7cd6a2fb1311170183ebd11634344354))
* stop the home lazy loader at the last page of sections ([b018ba2](https://github.com/JellyUX/Homepage/commit/b018ba29b56d910901a33cd8e8b38355d9e1d224))

## [2.9.2](https://github.com/JellyUX/Homepage/compare/v2.9.1...v2.9.2) (2026-10-11)

### Bug Fixes

* cache empty results of library-computed caches ([a4efdf7](https://github.com/JellyUX/Homepage/commit/a4efdf78e6bb5b571d525462119cb5f6fb27135d))
* clear scoring snapshots when the configuration is saved ([f2e8cea](https://github.com/JellyUX/Homepage/commit/f2e8cea0529b7c3d1abc433c5cfe6bfbdf01b26b))
* compute series progress from watched series only ([e321f36](https://github.com/JellyUX/Homepage/commit/e321f368055cbd28b91367e1c2457339d9236a07))
* describe what the session cache TTL actually controls ([66385cc](https://github.com/JellyUX/Homepage/commit/66385cc42ba99fadf0ef456a11130bb6433fe46d))
* expire cached home layouts a fixed time after they are built ([02de283](https://github.com/JellyUX/Homepage/commit/02de283cc7345f5d1746cb0296887b3cadfa0a60))
* find watched series through played episodes and date them by the latest one ([b91e9ff](https://github.com/JellyUX/Homepage/commit/b91e9ff225c022e9799309ae82bbd7dbfe64c192))
* keep each user's scoring snapshot for the whole day ([72cebad](https://github.com/JellyUX/Homepage/commit/72cebad95a88378b52c45e79035b034998da6821))
* keep parsed disk caches in memory between reads ([6373939](https://github.com/JellyUX/Homepage/commit/6373939cf08e1e2ba76904a96bee24743b923c22))
* keep the watchlist to movies and series and stop its page-load scan ([43fbb17](https://github.com/JellyUX/Homepage/commit/43fbb1794e299310a0b8dc72e22213258f9ca95a))
* keep TMDb to IMDb id mappings on disk ([16b9e57](https://github.com/JellyUX/Homepage/commit/16b9e5711b6ce9997a5da5778e8aeca10ce7136c))
* load ids or image-less entities where nothing else is read ([f9db7d1](https://github.com/JellyUX/Homepage/commit/f9db7d14bbd839b264878f128018a44ba7a35397))
* match TMDb items on the local library before asking TMDb ([26e048b](https://github.com/JellyUX/Homepage/commit/26e048b1170a03a964cabbcce137d475724d80c9))
* never block the home page on a scoring recompute ([7c3519f](https://github.com/JellyUX/Homepage/commit/7c3519f2868043b7a33e32dca0a86f48b3033efe))
* put the detail watchlist button on the visible page ([827a4d2](https://github.com/JellyUX/Homepage/commit/827a4d26442016acc3fa31112951535a31ffff93))
* read people of the 100 most recent titles only when scoring ([c4d0c34](https://github.com/JellyUX/Homepage/commit/c4d0c34cc0615eb66672ec12445405a696fdb15e))
* refresh a user's scoring after they finish or mark a title played ([9b5bd61](https://github.com/JellyUX/Homepage/commit/9b5bd61a90f7fc9885f4f6f5151ebde6b706df64))
* refresh stale TMDb caches after the server has started ([2e3f31a](https://github.com/JellyUX/Homepage/commit/2e3f31a339ce68089d2e508f63aa5e7e80e8e74c))
* refresh the collections index when a collection changes ([2fa1f75](https://github.com/JellyUX/Homepage/commit/2fa1f75534c197cb2e2d44cdd530c26782e60a4b))
* run scoring computations on dedicated threads ([a30e36e](https://github.com/JellyUX/Homepage/commit/a30e36ea12d054ffc30d2e4c0c0a028567e2749d))
* score a user's first access from 25 titles, then all 100 in the background ([a0190eb](https://github.com/JellyUX/Homepage/commit/a0190eb885ffdf4f6e39e4fed486ed9c9c44bb08))
* stop counting every match for home sections ([5f88ec3](https://github.com/JellyUX/Homepage/commit/5f88ec37f8eedb8eebb02364204ba22bc2a40eb3))

## [2.9.1](https://github.com/JellyUX/Homepage/compare/v2.9.0...v2.9.1) (2026-10-11)

### Bug Fixes

* apply user library rights to cached-id sections ([ab495c9](https://github.com/JellyUX/Homepage/commit/ab495c9199b42fef2480915095fcf3c9a420d62d))
* derive the userId of per-user endpoints from the caller's token ([d98e430](https://github.com/JellyUX/Homepage/commit/d98e430e941d27e0b70129a6c76bf504ce0bc415))
* give user configuration files their own schema version ([ff8f342](https://github.com/JellyUX/Homepage/commit/ff8f34204f6f1856baffe7f6933151feba1963b7))
* load plugin assets relative to the server BaseUrl ([561da80](https://github.com/JellyUX/Homepage/commit/561da80925caee6147df0cb92b3150ab821aec1c))
* pair each user override with a single home row ([153607f](https://github.com/JellyUX/Homepage/commit/153607f6e4d182e736a726ed16f6658efd318c0d))
* remove the external widget-pack DLL loader ([84b9ffa](https://github.com/JellyUX/Homepage/commit/84b9ffa7b0b2e8c7713eacbf930b18d9f09ba3bf))
* return an empty section when an admin widget value does not resolve ([0cf280a](https://github.com/JellyUX/Homepage/commit/0cf280a0433f6eafabbd8b5a23753a560b645b84))
* seed all native widgets on a fresh install ([8b400b6](https://github.com/JellyUX/Homepage/commit/8b400b63ec9ba2f167dc4bbe916893be83e0facc))

## [2.9.0](https://github.com/JellyUX/Homepage/compare/v2.8.0...v2.9.0) (2026-07-26)

### Features

* complete V3 feature set ([a368dcc](https://github.com/JellyUX/Homepage/commit/a368dccc1e2249111a8f7ab367afeade65629188))

## [2.8.0](https://github.com/JellyUX/Homepage/compare/v2.7.0...v2.8.0) (2026-07-26)

### Features

* add Recently Added Episodes widget ([1bf3f94](https://github.com/JellyUX/Homepage/commit/1bf3f9479cd5ccb3705f4ed5217d18f5c6325fbd))

## [2.7.0](https://github.com/JellyUX/Homepage/compare/v2.6.1...v2.7.0) (2026-07-26)

### Features

* add randomized selection to Because You Watched sections ([775144b](https://github.com/JellyUX/Homepage/commit/775144b9aeb90b2bc1a78134faac35e9ff4b96c4))
* add seasonal home screen widget with presets ([02e89db](https://github.com/JellyUX/Homepage/commit/02e89db4916b17b384595bea69cba6308425ab5d))

## [2.6.1](https://github.com/JellyUX/Homepage/compare/v2.6.0...v2.6.1) (2026-07-25)

### Bug Fixes

* remove unreliable JellyUX tab shortcuts feature ([7b4ca82](https://github.com/JellyUX/Homepage/commit/7b4ca820304416a72184d3d882b3b05cb29a639f))

## [2.6.0](https://github.com/JellyUX/Homepage/compare/v2.5.3...v2.6.0) (2026-07-25)

### Features

* add breadcrumb navigation ([104ff7f](https://github.com/JellyUX/Homepage/commit/104ff7f9c08e456952e9f75896a0f56863ef3e8e)), closes [#moviesTab](https://github.com/JellyUX/Homepage/issues/moviesTab)
* add JellyUX tab shortcuts to the navigation drawer ([b21259b](https://github.com/JellyUX/Homepage/commit/b21259b4c91fdaa56e7a04fec354389b169429b6))

## [2.5.3](https://github.com/JellyUX/Homepage/compare/v2.5.2...v2.5.3) (2026-07-23)

### Bug Fixes

* correct library infinite scroll after in-app navigation to the page ([c7247f7](https://github.com/JellyUX/Homepage/commit/c7247f7c0a82376baea01dd25c948264f198cf43))
* stamp the real release version onto the compiled assembly ([0858533](https://github.com/JellyUX/Homepage/commit/08585331b723e7cb36622dd1612674ae5aed4f01))

## [2.5.2](https://github.com/JellyUX/Homepage/compare/v2.5.1...v2.5.2) (2026-07-23)

### Bug Fixes

* align injected scroller cards with native sections via no-padding ([c45680c](https://github.com/JellyUX/Homepage/commit/c45680c130ab38f38376c82749ed06ee8419bc08))
* don't hide native pagination on library pages until cardBuilder is ready ([5cb56d7](https://github.com/JellyUX/Homepage/commit/5cb56d79971cad21b0345b1ecfc1139496472c36))

## [2.5.1](https://github.com/JellyUX/Homepage/compare/v2.5.0...v2.5.1) (2026-07-23)

### Bug Fixes

* match native section sizing for flattened seasons and Included In ([a6ea5a8](https://github.com/JellyUX/Homepage/commit/a6ea5a835a488af6fa9bc0d71a93c10060d29ac7))
* trigger library infinite scroll well before the literal page bottom ([50d7f89](https://github.com/JellyUX/Homepage/commit/50d7f89389dac99e2828978ec85e374762c2354b))

## [2.5.0](https://github.com/JellyUX/Homepage/compare/v2.4.1...v2.5.0) (2026-07-22)

### Features

* add infinite scroll to library pages and fix eager lazy-load fetch ([c4e5f35](https://github.com/JellyUX/Homepage/commit/c4e5f35533de504f0694457c72bc9b2e28bb1912))
* flatten single-season shows and show episodes on the series page ([908fc7d](https://github.com/JellyUX/Homepage/commit/908fc7dc8340171eb471a1453c5dc7ad42fa0155)), closes [#listChildrenCollapsible](https://github.com/JellyUX/Homepage/issues/listChildrenCollapsible) [#childrenCollapsible](https://github.com/JellyUX/Homepage/issues/childrenCollapsible)
* show parent collections on the details page and add collection sorting ([b62ee90](https://github.com/JellyUX/Homepage/commit/b62ee908703e3a1c49f9909eb584ed940be9b09b))

### Bug Fixes

* prevent duplicate sections and a resort race in Phase 7.1/7.2 scripts ([48123b7](https://github.com/JellyUX/Homepage/commit/48123b7e0a79a53535cee6b198f8ca76e75a8071))
* resolve BoxSet member resolution in the collections index ([0f3fe8f](https://github.com/JellyUX/Homepage/commit/0f3fe8ff60e5ee8728e42cc7f28457058baa6e5d))

## [2.4.1](https://github.com/JellyUX/Homepage/compare/v2.4.0...v2.4.1) (2026-07-22)

### Bug Fixes

* repair broken native-style sort dialog and center empty-state messages ([6cdc1aa](https://github.com/JellyUX/Homepage/commit/6cdc1aaf53def5e3cf19f914fc0027cb55c97139))

## [2.4.0](https://github.com/JellyUX/Homepage/compare/v2.3.2...v2.4.0) (2026-07-22)

### Features

* add native Watchlist widget to the home screen ([cf0d211](https://github.com/JellyUX/Homepage/commit/cf0d211d56639be9980c34d2e9500b3982a939bf))
* add Series Progress, Movie History, and Statistics views ([cbb9f09](https://github.com/JellyUX/Homepage/commit/cbb9f09b7343c5c33f16cc5a3d0b83addd190a07))
* add shared UI helpers (empty state, loading spinner, progress bar, stat card, native sort dialog) ([0308376](https://github.com/JellyUX/Homepage/commit/0308376e8fec0a1e2a74fb14bec5ddd25fdb3946))
* replace inline-styled controls with native-style sort dialogs and visual polish across all tabs ([1a96ce4](https://github.com/JellyUX/Homepage/commit/1a96ce463bc207c785e0bcd14fc571f64b9a046d)), closes [#00a4dc](https://github.com/JellyUX/Homepage/issues/00a4dc) [#00a4dc](https://github.com/JellyUX/Homepage/issues/00a4dc)

### Bug Fixes

* switch to IUserManager.GetUsers()/GetUsersIds() and align SDK versions ([7b70732](https://github.com/JellyUX/Homepage/commit/7b707326b77aa531dd169a8d2b6ada955d612047))

## [2.3.2](https://github.com/JellyUX/Homepage/compare/v2.3.1...v2.3.2) (2026-07-21)

### Bug Fixes

* show the watchlist button on the item detail page ([bc77bde](https://github.com/JellyUX/Homepage/commit/bc77bde2b3d6b3237cc7d1dd0883c3428a10bc8b))

## [2.3.1](https://github.com/JellyUX/Homepage/compare/v2.3.0...v2.3.1) (2026-07-21)

### Bug Fixes

* revert to setAttribute for the injected tab buttons' is attribute ([c059504](https://github.com/JellyUX/Homepage/commit/c05950472d73421f6c0846fe7386c92ef6b4ed57))

## [2.3.0](https://github.com/JellyUX/Homepage/compare/v2.2.0...v2.3.0) (2026-07-21)

### Features

* add watchlist toggle to the item detail page and the More menu ([fb9616f](https://github.com/JellyUX/Homepage/commit/fb9616ffb235a921ac57c0d864e610bab7719b74))

### Bug Fixes

* reliably switch to injected home tabs on the first click ([e4cad1f](https://github.com/JellyUX/Homepage/commit/e4cad1feb868517e76d4f0fef1b33fb2d297cd8d))
* render cover art in the Watchlist tab ([a23d1b9](https://github.com/JellyUX/Homepage/commit/a23d1b934c94c580a655587dbde88be3af8f22d5))

## [2.2.0](https://github.com/JellyUX/Homepage/compare/v2.1.1...v2.2.0) (2026-07-21)

### Features

* add Watchlist tab with server-side filtering and sorting ([d184bd0](https://github.com/JellyUX/Homepage/commit/d184bd0d51a705bad90b15d75c69d4854af02594))
* add watchlist toggle button to card overlays ([f67b81c](https://github.com/JellyUX/Homepage/commit/f67b81c700a8dc26488d321dde4b5cacfe7ff8ae))
* automatically remove watched items from the watchlist ([2d66853](https://github.com/JellyUX/Homepage/commit/2d668531532e388a8fb9cb03290f177a92e4a04a))

## [2.1.1](https://github.com/JellyUX/Homepage/compare/v2.1.0...v2.1.1) (2026-07-20)

### Bug Fixes

* use distinct ids for JellyUX tab buttons and content panes ([17cc702](https://github.com/JellyUX/Homepage/commit/17cc7025c30b65cc4370fdc1db9ad89f3de9897c))

## [2.1.0](https://github.com/JellyUX/Homepage/compare/v2.0.3...v2.1.0) (2026-07-20)

### Features

* add generic server-side caches for exhaustive watch-history views ([ee7095e](https://github.com/JellyUX/Homepage/commit/ee7095e107a24b212aaa1f13aaffb93a5d23aabe))
* add internal tab injection mechanism for Watchlist and related screens ([bece83e](https://github.com/JellyUX/Homepage/commit/bece83ef85cd10c3df640a4567b402a58515d243))

## [2.0.3](https://github.com/JellyUX/Homepage/compare/v2.0.2...v2.0.3) (2026-07-20)

### Bug Fixes

* trigger release for Phase 3 cleanup batch ([94b25de](https://github.com/JellyUX/Homepage/commit/94b25dee6d41dddbb39b2a519a55f28c42b13e9a))

## [2.0.2](https://github.com/JellyUX/Homepage/compare/v2.0.1...v2.0.2) (2026-07-19)

### Bug Fixes

* handle JSON deserialization errors explicitly in TMDbApiClient ([47f3314](https://github.com/JellyUX/Homepage/commit/47f33149cf43539b9eb4d7ff7aa7826d610e88e0))
* include series in Because You Watched scoring and recommendations ([a23b6ba](https://github.com/JellyUX/Homepage/commit/a23b6ba9a31a38cc6f821dad97161705c7ac87f0))
* reject concurrent Rewards cache refresh requests ([c39aa99](https://github.com/JellyUX/Homepage/commit/c39aa992ed204825aac46225a576a505a9cf115f))

## [2.0.1](https://github.com/JellyUX/Homepage/compare/v2.0.0...v2.0.1) (2026-07-19)

### Bug Fixes

* validate Wikidata Q-id format to prevent SPARQL injection ([5a6b236](https://github.com/JellyUX/Homepage/commit/5a6b236b7c95617fe946e4631417365f87dbc807))

## [2.0.0](https://github.com/JellyUX/Homepage/compare/v1.8.2...v2.0.0) (2026-07-11)

### ⚠ BREAKING CHANGES

* the Personalized widget configuration schema changed -- MaxInstances is no
longer read by the widget engine for Personalized rows, replaced by a per-row rank
(SchemaVersion 1->2, Plugin.MigrateConfiguration). Existing configurations are migrated
automatically on load; no manual action is required.

### Features

* complete V2 feature set ([d9f55bd](https://github.com/JellyUX/Homepage/commit/d9f55bd834a4425bc97c3adefc54451eab176f76))

## [1.8.2](https://github.com/JellyUX/Homepage/compare/v1.8.1...v1.8.2) (2026-07-10)

### Bug Fixes

* enable automatic gzip/deflate decompression on the Wikidata HTTP client ([73b972d](https://github.com/JellyUX/Homepage/commit/73b972d20e0a4aaa806e644ae5bb2d45d8b00c70))

## [1.8.1](https://github.com/JellyUX/Homepage/compare/v1.8.0...v1.8.1) (2026-07-10)

### Bug Fixes

* harden WikidataApiClient error handling and surface failure details ([ce19449](https://github.com/JellyUX/Homepage/commit/ce194492f14d630f32ab55855e1a3e12f47609cd))

## [1.8.0](https://github.com/JellyUX/Homepage/compare/v1.7.0...v1.8.0) (2026-07-10)

### Features

* add Rewards cache service with library cross-referencing ([6a11a8e](https://github.com/JellyUX/Homepage/commit/6a11a8e0a687088f7cc61837d864c24acefe54d0))
* add Rewards widget and admin configuration UI ([5386527](https://github.com/JellyUX/Homepage/commit/53865271aab312014b7f4c5d9a06ad8990b67597))
* add Wikidata SPARQL client for structured award data ([26867ab](https://github.com/JellyUX/Homepage/commit/26867abc3f3b842f9f2ab35a83f0b6be08b6ac6b))

## [1.7.0](https://github.com/JellyUX/Homepage/compare/v1.6.2...v1.7.0) (2026-07-09)

### Features

* expose ordered widget category names via GET /JuxHomepage/meta ([77246a2](https://github.com/JellyUX/Homepage/commit/77246a2f4f387683ad2d50a7f3d84c3a947ea922))

## [1.6.2](https://github.com/JellyUX/Homepage/compare/v1.6.1...v1.6.2) (2026-07-09)

### Bug Fixes

* register FileTransformation before the best-effort TMDb refresh at startup ([09184bd](https://github.com/JellyUX/Homepage/commit/09184bdc0b4b9bcadb50e0e382981cc5ebf48511))

## [1.6.1](https://github.com/JellyUX/Homepage/compare/v1.6.0...v1.6.1) (2026-07-08)

### Bug Fixes

* surface silent loadSections drift and unhandled TMDb status types ([0df27ca](https://github.com/JellyUX/Homepage/commit/0df27ca58abb4c3a0d23367ec5d095cedea1a492))

## [1.6.0](https://github.com/JellyUX/Homepage/compare/v1.5.1...v1.6.0) (2026-07-08)

### Features

* compute per-row rank for Personalized widgets, replacing MaxInstances fan-out ([bb89e54](https://github.com/JellyUX/Homepage/commit/bb89e544649c48931521a2150097b53530288d7c))
* migrate existing MaxInstances configuration into independent rows ([833686a](https://github.com/JellyUX/Homepage/commit/833686a830c118d0f16b0a790910f3b735344dfd))
* update admin UI for the Personalized rank model ([5e46eec](https://github.com/JellyUX/Homepage/commit/5e46eec41e8436514eb71f8e7f0097c33ba4331d))

## [1.5.1](https://github.com/JellyUX/Homepage/compare/v1.5.0...v1.5.1) (2026-07-07)

### Bug Fixes

* move plugin data directories out of Jellyfin's scanned plugins tree ([96f0a76](https://github.com/JellyUX/Homepage/commit/96f0a761d777d9e95c382605c4269e78eb0bb4ff))

## [1.5.0](https://github.com/JellyUX/Homepage/compare/v1.4.0...v1.5.0) (2026-07-07)

### Features

* surface external widget-pack load failures in the admin config page ([0a1548a](https://github.com/JellyUX/Homepage/commit/0a1548a631dd30d4da101083f9821fa8c044f7bc))

### Bug Fixes

* scope external widget-pack loading to a dedicated folder with actionable errors ([479f748](https://github.com/JellyUX/Homepage/commit/479f74824227ee14e5ffd525ebb19a5d47d2146c))

## [1.4.0](https://github.com/JellyUX/Homepage/compare/v1.3.1...v1.4.0) (2026-07-06)

### Features

* log unresolved cached library items in ConnectedWidgetBase ([e0ac15c](https://github.com/JellyUX/Homepage/commit/e0ac15cf68d248d1eb847309896409068a0c480d))

### Bug Fixes

* improve logging context and minor code quality cleanups ([77f3328](https://github.com/JellyUX/Homepage/commit/77f332851feca5a63212248a35c8de4a982509fc))

## [1.3.1](https://github.com/JellyUX/Homepage/compare/v1.3.0...v1.3.1) (2026-07-06)

### Bug Fixes

* log when a manual TMDb refresh request is rejected as already in progress ([3413211](https://github.com/JellyUX/Homepage/commit/3413211dee7f5d21736bc553ade41390ee38cf46))

## [1.3.0](https://github.com/JellyUX/Homepage/compare/v1.2.0...v1.3.0) (2026-07-06)

### Features

* add circuit breaker to TMDb HTTP client for sustained outages ([f0f39ce](https://github.com/JellyUX/Homepage/commit/f0f39cee08ff609373a7dd92bcb020e539f0a805))

### Bug Fixes

* make Top Rated vote count threshold configurable via discover ([7007a3c](https://github.com/JellyUX/Homepage/commit/7007a3c66dafb041712e8f6a5ab2000bbd34e481))
* reject concurrent manual TMDb refresh requests ([8a18671](https://github.com/JellyUX/Homepage/commit/8a18671f9364873c17906ebc0288065e8f058b41))

## [1.2.0](https://github.com/JellyUX/Homepage/compare/v1.1.0...v1.2.0) (2026-07-06)

### Features

* introduce IFileSystem abstraction for testable file access ([7e9fca9](https://github.com/JellyUX/Homepage/commit/7e9fca920319f7325727a8bd33de0b553e9f4a8f))

## [1.1.0](https://github.com/JellyUX/Homepage/compare/v1.0.2...v1.1.0) (2026-07-06)

### Features

* add SchemaVersion field and migration hook to configuration models ([18b72bd](https://github.com/JellyUX/Homepage/commit/18b72bd354af70d7ff9c96d9b33fc9e8f9ccef04))

### Bug Fixes

* validate configuration on load, not only on admin save ([383a8ef](https://github.com/JellyUX/Homepage/commit/383a8ef6c97596cadfedfda84fd8722996aa9a09))
* write user configuration atomically to avoid partial writes on crash ([62959c9](https://github.com/JellyUX/Homepage/commit/62959c9c88728613e51b7cf87dcbb313888f18fe))

## [1.0.2](https://github.com/JellyUX/Homepage/compare/v1.0.1...v1.0.2) (2026-07-06)

### Bug Fixes

* fall back to native rendering on any error inside the spliced loadSections fragment ([ecf2b8c](https://github.com/JellyUX/Homepage/commit/ecf2b8c5e5a49916cf7b2c9071fe36d53e46e9d1))
* guard reflective FileTransformation invocation with explicit error handling ([1aec924](https://github.com/JellyUX/Homepage/commit/1aec92472c8e58f126fcd34d5fbdb2b9c635f530))
* prevent IDOR by validating userId against the authenticated identity ([ca4ca4e](https://github.com/JellyUX/Homepage/commit/ca4ca4ec806cba6fbe44176f8da3f09d65b77724))

## [1.0.1](https://github.com/JellyUX/Homepage/compare/v1.0.0...v1.0.1) (2026-07-05)

### Bug Fixes

* replace plugin icon ([b0b8c56](https://github.com/JellyUX/Homepage/commit/b0b8c56ff865e380dbb71e0b8815e42f0dfde7e0))

## [1.0.0](https://github.com/JellyUX/Homepage/compare/v0.13.1...v1.0.0) (2026-07-04)

### ⚠ BREAKING CHANGES

* initial stable release, API surface is now stable

### Features

* complete V1 feature set ([27222c2](https://github.com/JellyUX/Homepage/commit/27222c247f4d4c25c5712445248ab233beae25c3))

## [0.13.1](https://github.com/JellyUX/Homepage/compare/v0.13.0...v0.13.1) (2026-07-04)

### Bug Fixes

* ensure BREAKING CHANGE commits trigger a major release ([7a28bf0](https://github.com/JellyUX/Homepage/commit/7a28bf092868ae28ec0020035220f7245ca84330))

## [0.13.0](https://github.com/JellyUX/Homepage/compare/v0.12.1...v0.13.0) (2026-07-04)

### Features

* enable JellyUX home rendering inside native shell clients ([9c0ac77](https://github.com/JellyUX/Homepage/commit/9c0ac7728e447bafa247fefa28ac0cac0072bbb3))

## [0.12.1](https://github.com/JellyUX/Homepage/compare/v0.12.0...v0.12.1) (2026-07-04)

### Bug Fixes

* improve error handling and logging robustness ([4e2d3c2](https://github.com/JellyUX/Homepage/commit/4e2d3c220a6781e81821858a8fdc589e190ed3d6))

## [0.12.0](https://github.com/JellyUX/Homepage/compare/v0.11.2...v0.12.0) (2026-07-03)

### Features

* add French and English language files ([3b114b2](https://github.com/JellyUX/Homepage/commit/3b114b20d3bc6b9889e7ce849add3a0ffaec2b24))
* add localization service with JSON language files ([c7494b2](https://github.com/JellyUX/Homepage/commit/c7494b226bdb94ce23d8f467beba1cb6aac7e234))
* apply i18n to all widgets and admin UI ([b4ffec1](https://github.com/JellyUX/Homepage/commit/b4ffec175a30a8a7970bcba87439fa503fb1670f))

## [0.11.2](https://github.com/JellyUX/Homepage/compare/v0.11.1...v0.11.2) (2026-07-03)

### Bug Fixes

* deduplicate TMDb list items by id before caching ([28ce003](https://github.com/JellyUX/Homepage/commit/28ce00367ca00002765357f74ff8380d51a72b19))
* remove Upcoming Movies widget (unusable in the Connected model, never matches local library) ([a492557](https://github.com/JellyUX/Homepage/commit/a492557f9c2813bccf5af9f9373d764024608b9a))

## [0.11.1](https://github.com/JellyUX/Homepage/compare/v0.11.0...v0.11.1) (2026-07-02)

### Bug Fixes

* broaden On TV widget results using tv/on_the_air instead of tv/airing_today ([74543a3](https://github.com/JellyUX/Homepage/commit/74543a34a3746fb6c89c8b9474db27bca7962945))
* stop background TMDb status poll from wiping unsaved Pages/Region edits ([5dd8c0a](https://github.com/JellyUX/Homepage/commit/5dd8c0a83e67903790e4a27d2fcebb7f2d5b997b))

## [0.11.0](https://github.com/JellyUX/Homepage/compare/v0.10.1...v0.11.0) (2026-07-02)

### Features

* add configurable pagination to TMDb list fetches ([f1868e1](https://github.com/JellyUX/Homepage/commit/f1868e14532babaaa6d8b0070a2d12911441548a))
* add customizable DiscoverMoviesWidget with TMDb autocomplete ([24d9db7](https://github.com/JellyUX/Homepage/commit/24d9db75f3ce50f712bfdf0daed425d7c048d64a))
* add TopRatedMoviesWidget, TopRatedShowsWidget, and NowPlayingMoviesWidget ([3b86684](https://github.com/JellyUX/Homepage/commit/3b866845efc1bc514c3978e0d129683f8e280e8d))

### Bug Fixes

* match widget config row by instance value in on-demand section fetch ([bb9261d](https://github.com/JellyUX/Homepage/commit/bb9261dd38d0bd5bbe6552299dc9b98e90dfcf62))

## [0.10.1](https://github.com/JellyUX/Homepage/compare/v0.10.0...v0.10.1) (2026-07-02)

### Bug Fixes

* preserve existing TMDb cache instead of overwriting with an empty result ([7d148fb](https://github.com/JellyUX/Homepage/commit/7d148fb30146675d023ec018846e318b59a83854))

## [0.10.0](https://github.com/JellyUX/Homepage/compare/v0.9.1...v0.10.0) (2026-07-02)

### Features

* add TMDb refresh status and manual trigger in admin UI ([557a5ff](https://github.com/JellyUX/Homepage/commit/557a5ffe008085b8304c8c0dd657705b9651337d))
* add TrendingMoviesWidget ([3386e63](https://github.com/JellyUX/Homepage/commit/3386e635ed0f09806147f9ccab6be0b999740ddf))
* add TrendingShowsWidget, AiringTodayShowsWidget, UpcomingMoviesWidget ([4da93dc](https://github.com/JellyUX/Homepage/commit/4da93dc089022727631e8b3d6dfccda25929aaa2))

## [0.9.1](https://github.com/JellyUX/Homepage/compare/v0.9.0...v0.9.1) (2026-07-02)

### Bug Fixes

* log explicit per-type item and library-match counts on TMDb refresh ([02faa10](https://github.com/JellyUX/Homepage/commit/02faa1011c92a5838e69d21a13a0c822922c40c0))

## [0.9.0](https://github.com/JellyUX/Homepage/compare/v0.8.0...v0.9.0) (2026-07-02)

### Features

* add TMDb cache service with library cross-referencing ([0bea103](https://github.com/JellyUX/Homepage/commit/0bea103046ce0269333f34566c4a5e2fb6eccfa3))
* add TMDb daily refresh scheduled task ([a35b023](https://github.com/JellyUX/Homepage/commit/a35b0234bc59a16164989ae95691c54e0ae44e49))
* add TMDb HTTP client with error handling ([a8e5496](https://github.com/JellyUX/Homepage/commit/a8e54962271cc167abde34103436fc6210363d93))

## [0.8.0](https://github.com/JellyUX/Homepage/compare/v0.7.1...v0.8.0) (2026-07-01)

### Features

* add BecauseYouWatchedWidget ([65843dc](https://github.com/JellyUX/Homepage/commit/65843dcb430fec9ec8c1cfa6cef728f70fd34561))
* add FavoriteActorWidget and FavoriteDirectorWidget ([caa693f](https://github.com/JellyUX/Homepage/commit/caa693fbbeac2b6ab4876ea7a88f0d5176798102))
* add PersonalizedWidget base class and FavoriteGenreWidget ([2744341](https://github.com/JellyUX/Homepage/commit/27443413cfb9482e06107f0c4cf762968977d821))
* add ScoringService for user preference analysis ([a5a85f0](https://github.com/JellyUX/Homepage/commit/a5a85f051296bc0152e6cda1b5523ed810a55c43))
* emit one descriptor per widget instance from CreateInstances fan-out ([4b0bac6](https://github.com/JellyUX/Homepage/commit/4b0bac69cc4d1a6251a2e3287555218eb25a2134))
* register personalized widgets and add management UI ([a73bdcd](https://github.com/JellyUX/Homepage/commit/a73bdcd2a5f833726ee41cede2ebd96f9e9ff44e))

## [0.7.1](https://github.com/JellyUX/Homepage/compare/v0.7.0...v0.7.1) (2026-07-01)

### Bug Fixes

* add search-as-you-type autocomplete and restore collection label on reload ([57a9a74](https://github.com/JellyUX/Homepage/commit/57a9a740cdd08a8b752587e6c3b7c0791364c96c))
* resolve collection items via BoxSet linked children instead of AncestorIds ([43a2d28](https://github.com/JellyUX/Homepage/commit/43a2d28142cc9a70ccc08209f7d5488f3af02b7a))

## [0.7.0](https://github.com/JellyUX/Homepage/compare/v0.6.2...v0.7.0) (2026-07-01)

### Features

* add ActorWidget and DirectorWidget ([430fdcf](https://github.com/JellyUX/Homepage/commit/430fdcf32af9e611fd9d66e23b81325bb211dd8d))
* add admin widget management UI with value autocomplete ([276c952](https://github.com/JellyUX/Homepage/commit/276c952357e6f456abad41fc69a57438edecb098))
* add AdminWidget base class with library value provider ([b77fa9e](https://github.com/JellyUX/Homepage/commit/b77fa9ea18ee80e80c9a2aa5fc7d6e19311c62d9))
* add GenreWidget ([8a0b896](https://github.com/JellyUX/Homepage/commit/8a0b896855a4c4ac72b69e842b3d49dbc92d5efc))
* add StudioWidget, CollectionWidget, TagWidget, YearWidget ([8b9ac85](https://github.com/JellyUX/Homepage/commit/8b9ac85ff3300baa4531494a93cc12f217d3888e))
* add widget values endpoint for admin autocomplete ([d85b2a2](https://github.com/JellyUX/Homepage/commit/d85b2a2a066e7c97e32b7a35481ab830f292cefe))
* propagate per-instance AdditionalData through widget layout ([62a76dc](https://github.com/JellyUX/Homepage/commit/62a76dc98dd6eaeb7fcb6af3a4bbb684c96e2a05))

## [0.6.2](https://github.com/JellyUX/Homepage/compare/v0.6.1...v0.6.2) (2026-06-30)

### Bug Fixes

* strip remaining is=emby-* attributes to silence webcomponents polyfill errors ([76c18d3](https://github.com/JellyUX/Homepage/commit/76c18d39bf9b7e642a6df4ac5fddfbd38d24291b))

## [0.6.1](https://github.com/JellyUX/Homepage/compare/v0.6.0...v0.6.1) (2026-06-30)

### Bug Fixes

* remove is=emby-checkbox to prevent htmlFor error in webcomponents polyfill ([199844a](https://github.com/JellyUX/Homepage/commit/199844a9bc584d7ecd4624acfa7ca3e2b01a69c0))

## [0.6.0](https://github.com/JellyUX/Homepage/compare/v0.5.1...v0.6.0) (2026-06-30)

### Features

* add admin configuration page with widget management UI ([754b793](https://github.com/JellyUX/Homepage/commit/754b793708611b236a27a38a578b2ecaecdca9da))
* invalidate session cache when configuration changes ([46df34e](https://github.com/JellyUX/Homepage/commit/46df34e31dc5dd46468dd287f3b4900278d2d7ba))

## [0.5.1](https://github.com/JellyUX/Homepage/compare/v0.5.0...v0.5.1) (2026-06-30)

### Bug Fixes

* resolve broken see-all links for recently-added sections ([5e6ec17](https://github.com/JellyUX/Homepage/commit/5e6ec173d26edc4ea3c60dad92f33b1b21da7a11))

## [0.5.0](https://github.com/JellyUX/Homepage/compare/v0.4.0...v0.5.0) (2026-06-30)

### Features

* add client detection and JellyfinAPI global attachment ([cad02fc](https://github.com/JellyUX/Homepage/commit/cad02fcf622af8f27582f0d9db194093c7a37894))
* implement loadSections override with API-driven widget rendering ([961fda5](https://github.com/JellyUX/Homepage/commit/961fda53431e5d3e2bfcce416b5c5c0e87e2bbb4))

## [0.4.0](https://github.com/JellyUX/Homepage/compare/v0.3.1...v0.4.0) (2026-06-29)

### Features

* add ContinueWatchingWidget ([fd73acd](https://github.com/JellyUX/Homepage/commit/fd73acd6a8591dce3fb7f6e5c957efbd68833bc5))
* add MyMediaWidget ([04626ed](https://github.com/JellyUX/Homepage/commit/04626ed3e80580057a0107c5a58b7d7ffa170a0b))
* add NativeWidgetBase shared base class for native widgets ([68a41e4](https://github.com/JellyUX/Homepage/commit/68a41e4a4c8d45a19bd5db3c10f4944ebe9b51b4))
* add NextUpWidget ([3db6221](https://github.com/JellyUX/Homepage/commit/3db6221af8dc2fdab4a81ba4c84da364adb19576))
* add RecentlyAddedMoviesWidget and RecentlyAddedShowsWidget ([2f3bd63](https://github.com/JellyUX/Homepage/commit/2f3bd63833a2f9cf854cb158dfdcc12c105e3140))
* register native widgets with default configuration ([594df9f](https://github.com/JellyUX/Homepage/commit/594df9fecb6a81bb7a8ce53cf13490569b561996))

## [0.3.1](https://github.com/JellyUX/Homepage/compare/v0.3.0...v0.3.1) (2026-06-29)

### Bug Fixes

* replace Dictionary ExtraParams with WidgetExtraParam array for XmlSerializer compatibility ([ed67721](https://github.com/JellyUX/Homepage/commit/ed677215aa319d8b0f37624b5914ea9b44c63eb1))

## [0.3.0](https://github.com/JellyUX/Homepage/compare/v0.2.0...v0.3.0) (2026-06-29)

### Features

* add API endpoints for sections, items and configuration ([8222a75](https://github.com/JellyUX/Homepage/commit/8222a75443d19c2ef380ca0895b8deee772f6f41))
* add complete plugin and user configuration models ([891de7c](https://github.com/JellyUX/Homepage/commit/891de7c7c73d4de6724939ac253464656a893e25))
* add widget engine core interfaces and models ([f42bece](https://github.com/JellyUX/Homepage/commit/f42bece0dba534cfdde3ca932ef610d49345b9c7))
* add widget registry with external DLL discovery ([4703498](https://github.com/JellyUX/Homepage/commit/47034986986fcdf06ac9952bd6714c52049c25b8))
* add widget service with session cache and MinItems filtering ([27d226a](https://github.com/JellyUX/Homepage/commit/27d226a85648d85d3cfe5478d0107fdaa8ab74d1))

## [0.2.0](https://github.com/JellyUX/Homepage/compare/v0.1.1...v0.2.0) (2026-06-28)

### Features

* add controller endpoints for web resources and meta ([47df8a9](https://github.com/JellyUX/Homepage/commit/47df8a910bf7feac8e5679c8570f4f643f63fb94))
* add embedded web resource placeholders ([8721595](https://github.com/JellyUX/Homepage/commit/8721595000deb0aba5f20bc9e245010bdeec0bd4))
* add FileTransformation detector with explicit error on missing dependency ([55afd38](https://github.com/JellyUX/Homepage/commit/55afd3876d45d250edc3f38e0a2e63e52f76e361))
* inject CSS and JS into index.html via FileTransformation ([6ca0f74](https://github.com/JellyUX/Homepage/commit/6ca0f7406af0829d4e95ac5689678710b73d7dd5))

## [0.1.1](https://github.com/JellyUX/Homepage/compare/v0.1.0...v0.1.1) (2026-06-28)

### Bug Fixes

* correct sourceUrl in manifest and plugin-url in release workflow ([9a3769a](https://github.com/JellyUX/Homepage/commit/9a3769af4d9969023e254ac67a892c752d65c5ad))
* remove stale task reference comment from PluginServiceRegistrator ([0442ed2](https://github.com/JellyUX/Homepage/commit/0442ed24f1fc02bba2945f3e091026830ffa4936))

# Changelog

<!-- Entries above this line are generated automatically by semantic-release. -->

## [0.1.0] - 2026-06-28

### Added

- .NET 9 solution (`JellyUX-Homepage.sln`) and `Jellyfin.Plugin.JuxHomepage` project (net9.0, Jellyfin 10.11 NuGet packages)
- `Plugin.cs` - plugin entry point inheriting `BasePlugin<PluginConfiguration>` (GUID, Name, Description)
- `PluginConfiguration.cs` - empty configuration class, to be populated in Phase 3
- `PluginServiceRegistrator.cs` - DI service registrator stub implementing `IPluginServiceRegistrator`
- `.gitignore` exceptions for `src/` (source files tracked, `bin/` and `obj/` ignored)
- GitHub Actions CI workflow (`build.yml`) with conditional guard (no-op until Phase 1 adds a .sln)
- GitHub Actions release workflow (`release.yml`) using JPRM for plugin packaging and MD5 checksum
- `build.yaml` - JPRM plugin metadata (GUID: 3adf1f1f-1541-4e47-b9e3-34d2d2968af6, targetAbi 10.11.0.0, net9.0)
- `manifest.json` - initial Jellyfin plugin repository manifest (empty versions, filled by release workflow)
- `README.md` - project overview, installation instructions, widget table, compatibility notes
- `CONTRIBUTING.md` - development setup, code conventions, No AI Slope design rules, widget contribution guide
- `LICENSE` - GNU General Public License v3.0
