#!/usr/bin/env python3
"""
Called by `node ci/plugin-package.mjs build`, before `jprm plugin build` of each Jellyfin line. Patches the compiled assembly's
own AssemblyVersion/FileVersion/Version to match the release version.

Without this, the .csproj's hardcoded "0.1.0.0" ships in every release's DLL regardless of the
actual plugin version (manifest.json / build.yaml / the release tag all update correctly via jprm,
but the compiled assembly itself never did). TransformationPatches.IndexHtml reads
Plugin.Instance.Version -- which .NET resolves to this exact AssemblyVersion -- to build the
`?v=...` cache-busting query string on every injected <script>/<link> tag. Since that version never
changed, every release's JS/CSS was served from browser cache indefinitely (up to the
`Cache-Control: max-age=3600` response header) after an update, hard refresh or not depending on
timing -- confirmed live on jellyux-test (TODO_V3.md Phase 7 manual testing) as the root cause of
several "the fix isn't working" reports that were actually stale cached JS.

Usage: python3 .github/scripts/set-plugin-version.py <version>  (e.g. "2.18.0.10": the release
version and the Jellyfin line as 4th component, ci/jellyfin-lines.json versionSuffix)
"""
import re
import sys

CSPROJ_PATH = "src/Jellyfin.Plugin.JuxHomepage/Jellyfin.Plugin.JuxHomepage.csproj"


def main() -> None:
    if len(sys.argv) < 2:
        print("Usage: set-plugin-version.py <version>", file=sys.stderr)
        sys.exit(1)

    version_4parts = sys.argv[1]  # e.g. "2.18.0.10"
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version_4parts):
        print(f"ERROR: expected a 4-part version, got {version_4parts}", file=sys.stderr)
        sys.exit(1)

    with open(CSPROJ_PATH, encoding="utf-8") as f:
        content = f.read()

    patched = content
    for tag in ("AssemblyVersion", "FileVersion", "Version"):
        patched, count = re.subn(
            rf"<{tag}>[^<]*</{tag}>",
            f"<{tag}>{version_4parts}</{tag}>",
            patched,
        )
        if count != 1:
            print(f"ERROR: expected exactly one <{tag}> element in {CSPROJ_PATH}, found {count}", file=sys.stderr)
            sys.exit(1)

    with open(CSPROJ_PATH, "w", encoding="utf-8") as f:
        f.write(patched)

    print(f"{CSPROJ_PATH} version fields set to {version_4parts}")


if __name__ == "__main__":
    main()
