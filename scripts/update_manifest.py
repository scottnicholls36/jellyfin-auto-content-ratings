#!/usr/bin/env python3
"""Adds a release to manifest.json, the plugin repository file Jellyfin reads.

Usage: update_manifest.py <manifest> <zip> <version> <source-url> <target-abi> [changelog]
"""
import datetime
import hashlib
import json
import sys


def main():
    manifest_path, zip_path, version, source_url, target_abi = sys.argv[1:6]
    changelog = sys.argv[6] if len(sys.argv) > 6 else ""

    with open(zip_path, "rb") as f:
        # Jellyfin verifies downloads against an MD5 checksum.
        checksum = hashlib.md5(f.read()).hexdigest()

    with open(manifest_path, encoding="utf-8") as f:
        manifest = json.load(f)

    package = manifest[0]
    # Re-running a release replaces its entry rather than duplicating it.
    versions = [v for v in package["versions"] if v["version"] != version]
    versions.insert(0, {
        "version": version,
        "changelog": changelog,
        "targetAbi": target_abi,
        "sourceUrl": source_url,
        "checksum": checksum,
        "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    })
    versions.sort(key=lambda v: tuple(int(p) for p in v["version"].split(".")), reverse=True)
    package["versions"] = versions

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")

    print(f"Added {version} ({checksum}) to {manifest_path}")


if __name__ == "__main__":
    main()
