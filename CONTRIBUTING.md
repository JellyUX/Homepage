# Contributing to JellyUX Homepage

Thank you for your interest in contributing. This document covers the development setup, conventions, and workflow.

---

## Development Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (x64)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Git

---

## Environment Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/JellyUX/Homepage.git
   cd Homepage
   ```

2. Start the test Jellyfin server:
   ```bash
   docker compose -f docker/docker-compose.yml up -d
   ```
   Copy `docker/.env.example` to `docker/.env` and adjust the media paths before the first run.

3. Build and deploy the plugin to the test container (PowerShell):
   ```powershell
   .\docker\scripts\deploy-plugin.ps1
   ```
   This builds the plugin, copies the DLL to the container's plugin directory, and restarts Jellyfin.

4. Open `http://localhost:8096` to verify the plugin is loaded.

---

## Code Conventions

- **Language**: All code, variable names, comments, commit messages, and documentation must be written in **English**.
- **Commits**: Follow [Conventional Commits](https://www.conventionalcommits.org/):
  - Types: `feat`, `fix`, `refactor`, `style`, `docs`, `chore`, `test`
  - One atomic commit per logical change
  - Example: `feat: add ContinueWatching widget`
- **No commented-out code**: Dead code must be deleted, not commented.
- **CSS prefix**: All plugin CSS classes must use the `jux-` prefix.
- **CSS variables**: Reuse native Jellyfin CSS variables (`var(--primaryTextColor)`, `var(--accent)`, etc.) instead of hardcoding colors.

---

## Design Rules - No AI Slope

The following design elements are **strictly forbidden** in any contributed code, markup, or assets:

### Banned fonts

Inter, Poppins, Manrope, Outfit, Plus Jakarta Sans, Space Grotesk

### Banned colors and patterns

- Blue-violet or violet-cyan gradients
- Tailwind Blue `#3B82F6`
- Background Gray-50 `#F9FAFB`
- Black + neon violet combination
- Turquoise accent on dark background

### Typography

- No em dashes (the long dash character). Use regular hyphens or restructure the sentence.

---

## Adding a Widget

1. Extend the base class matching your widget's category rather than implementing `IWidget`
   directly:
   - `NativeWidgetBase` for `Widgets/Native/`
   - `AdminWidgetBase` (or `PersonWidgetBase` for actor/director widgets) for `Widgets/Admin/`
   - `PersonalizedWidgetBase` (or `PersonalizedPersonWidgetBase`) for `Widgets/Personalized/`
   - `ConnectedWidgetBase<T>` for `Widgets/Connected/` (TMDb-backed widgets)
2. Name the `WidgetType` following the `jux.<category>.<kebab-case-name>` convention (e.g.
   `jux.connected.discover-movies`).
3. Register it in `PluginServiceRegistrator.RegisterServices()` via the `RegisterNativeWidget<TWidget>()`
   helper - used for every category despite its name - in the position matching the desired display
   order.
4. Add a localization key for both `fr.json` and `en.json` under `src/.../Localization/`, using the
   `WidgetType` string itself as the key. Personalized widgets also need a `.format` key (e.g.
   `jux.personalized.favorite-genre.format` = "More {value}").
5. For Connected/TMDb widgets: don't call the TMDb API directly from the widget. Read from
   `ITMDbCacheService`, which is refreshed separately by a scheduled task, and requires a TMDb API key
   configured in the plugin settings.
6. Document the widget in the README widget table.

Third-party widget packs (separate DLLs implementing `IWidget`, dropped into the plugin's config
directory) are auto-discovered by the registrar - useful if you're building an external extension
rather than contributing to this repository directly.

---

## Adding a Language

Every label lives in `src/Jellyfin.Plugin.JuxHomepage/Localization/<lang>.json` (`en.json`, `fr.json`):
widget names, the admin page (`admin.*`) and the client scripts (`client.*`). To add a language, copy
`en.json` to `<lang>.json` (the short code of the web client language, e.g. `de.json`) and translate the
values; a key you leave out falls back to English. Every embedded file is picked up as a language, and
the client scripts receive the `client.*` keys through `jux-i18n.js`. Never write a label in a script.

## Testing

**Automated tests** (once available):
```bash
dotnet test
```

**Manual test procedure**:
1. Run `.\docker\scripts\deploy-plugin.ps1` to deploy the latest build.
2. Open `http://localhost:8096` and verify the home screen loads correctly.
3. Check the browser console for errors.
4. Test widget configuration from the admin panel.

---

## Jellyfin Update Test Procedure

1. Bump the `jellyfin/jellyfin` image in `docker/docker-compose.yml`, then run `docker compose -f
   docker/docker-compose.yml up -d`.
2. Follow the "Jellyfin Update Procedure" in `CLAUDE.md` to confirm the `loadSections` chunk is still
   detected and patched.
3. Reload the home page: if the JellyUX sections do not render, check the browser console (prefix
   `[JellyUX]`) before suspecting the minified hook constants.
4. Re-run at least the Phase 12 manual tests (multi-client compatibility + robustness).

---

## Submitting a Contribution

1. Fork the repository and create a branch from `main`.
2. Make your changes following the conventions above.
3. Open a pull request against `main` with a clear description of what changed and why.
4. Ensure the CI build passes before requesting review.

Note: this repository uses `main` as the only permanent branch. Feature branches exist only in forks.
