#!/usr/bin/env python3
"""
Called by semantic-release prepareCmd after @semantic-release/changelog updates CHANGELOG.md.
Runs jprm repo add for the archive of each Jellyfin line (jellyux-homepage_<semver>.<line>.zip,
ci/plugin-package.mjs build), then injects the release notes into each of their manifest.json entries.
jprm keeps both entries: their versions differ by the 4th component (D-02).
Usage: python3 .github/scripts/update-manifest.py <semver>  (e.g. "2.18.0")
"""
import json
import os
import re
import subprocess
import sys


def get_repo_https_url() -> str:
    result = subprocess.run(
        ["git", "remote", "get-url", "origin"],
        capture_output=True, text=True, check=True,
    )
    url = result.stdout.strip()
    if url.endswith(".git"):
        url = url[:-4]
    if url.startswith("git@github.com:"):
        url = "https://github.com/" + url[len("git@github.com:"):]
    return url


def find_zips(artifacts_dir: str, version: str) -> list[str]:
    zips = sorted(f for f in os.listdir(artifacts_dir) if re.fullmatch(rf"[\w-]+_{re.escape(version)}\.\d+\.zip", f))
    if not zips:
        print(f"ERROR: no {version}.<line> zip found in {artifacts_dir}/", file=sys.stderr)
        sys.exit(1)
    return zips


def zip_version(filename: str) -> str:
    return filename.rsplit("_", 1)[1][: -len(".zip")]


def extract_changelog_section(version: str) -> str:
    try:
        with open("CHANGELOG.md", encoding="utf-8") as f:
            content = f.read()
    except FileNotFoundError:
        return ""

    # Matches: ## [x.y.z](url) (date)  OR  ## [x.y.z] (date)  OR  ## x.y.z (date)
    pattern = (
        r"##\s+\[?" + re.escape(version) + r"\]?[^\n]*\n"
        r"(.*?)(?=\n##\s+|\Z)"
    )
    m = re.search(pattern, content, re.DOTALL)
    return m.group(1).strip() if m else ""


def main() -> None:
    if len(sys.argv) < 2:
        print("Usage: update-manifest.py <semver>", file=sys.stderr)
        sys.exit(1)

    version = sys.argv[1]           # e.g. "2.18.0"
    tag = f"v{version}"             # e.g. "v2.18.0"
    artifacts_dir = "./artifacts"

    filenames = find_zips(artifacts_dir, version)
    versions_4parts = {zip_version(f) for f in filenames}  # e.g. {"2.18.0.10", "2.18.0.12"}
    repo_url = get_repo_https_url()
    for filename in filenames:
        plugin_url = f"{repo_url}/releases/download/{tag}/{filename}"
        subprocess.run(
            ["jprm", "repo", "add",
             f"--plugin-url={plugin_url}",
             "./", f"{artifacts_dir}/{filename}"],
            check=True,
        )

    release_notes = extract_changelog_section(version)
    if not release_notes:
        print(f"WARNING: no changelog section found for {version}", file=sys.stderr)

    with open("manifest.json", encoding="utf-8") as f:
        manifest = json.load(f)

    patched = set()
    for plugin in manifest:
        for v in plugin.get("versions", []):
            if v["version"] in versions_4parts:
                v["changelog"] = release_notes
                patched.add(v["version"])

    for missing in sorted(versions_4parts - patched):
        print(f"ERROR: {missing} not found in manifest.json", file=sys.stderr)
    if patched != versions_4parts:
        sys.exit(1)

    with open("manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=4, ensure_ascii=False)
        f.write("\n")

    chars = len(release_notes)
    print(f"manifest.json updated: {', '.join(sorted(versions_4parts))}, changelog {chars} chars")


if __name__ == "__main__":
    main()
