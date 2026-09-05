#!/usr/bin/env python3
"""FYAsset local build/test state reset utility.

Common ops:
  python CommandLine/fyasset_reset.py aa
  python CommandLine/fyasset_reset.py ab
  python CommandLine/fyasset_reset.py all
  python CommandLine/fyasset_reset.py all --keep-testruns
  python CommandLine/fyasset_reset.py all --dry-run

What it cleans (project-local only):
  - BuildData/Snapshots/<platform>/{AA|AB}
  - BuildData/Reports/{AA|AB} (optional with --reports)
  - HotfixOutput/Packages and root PackageIndex.json
  - StreamingAssets package exports (BuildIndex/manifests/bundles/catalog)
  - Assets/Build/Bootstrap/BuildIndex.json (if present)
  - VersionRecord -> 1.0.0 / Build 0
  - AA Hotfix group undo log
  - Permanent pipeline fixtures back to Full markers
  - HotfixPublish local service roots (optional)
  - HotfixOutput/TestRuns (optional)

Does NOT:
  - touch git history
  - deploy/clear Cloudflare public site
  - delete source assets outside known build/test ownership
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


def log(msg: str) -> None:
    print(f"[fyasset-reset] {msg}")


def rm_path(path: Path, dry_run: bool) -> None:
    if not path.exists():
        return
    if dry_run:
        log(f"DRY would remove: {path.relative_to(ROOT)}")
        return
    if path.is_file() or path.is_symlink():
        path.unlink(missing_ok=True)
    else:
        shutil.rmtree(path, ignore_errors=True)
    log(f"removed: {path.relative_to(ROOT)}")


def clear_dir_contents(path: Path, dry_run: bool) -> None:
    if not path.exists():
        return
    for child in path.iterdir():
        rm_path(child, dry_run)


def write_text(path: Path, content: str, dry_run: bool) -> None:
    if dry_run:
        log(f"DRY would write: {path.relative_to(ROOT)}")
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")
    log(f"wrote: {path.relative_to(ROOT)}")


def reset_version_database(dry_run: bool) -> None:
    path = ROOT / "Assets" / "Build" / "VersionRecord.asset"
    if not path.exists():
        log("VersionRecord.asset missing; skip")
        return
    text = path.read_text(encoding="utf-8")

    def set_field(src: str, field: str, value: str) -> str:
        return re.sub(
            rf"(^\s*{re.escape(field)}:\s*).*$",
            rf"\g<1>{value}",
            src,
            count=1,
            flags=re.M,
        )

    # Nested VersionNumber under CurrentVersion
    text = set_field(text, "Major", "1")
    text = set_field(text, "Minor", "0")
    text = set_field(text, "Patch", "0")
    text = set_field(text, "Build", "0")
    text = set_field(text, "Channel", '""')
    text = set_field(text, "LastBuildTime", '""')
    text = set_field(text, "DailyBuildCount", "0")
    if dry_run:
        log("DRY would reset VersionRecord to 1.0.0 / Build 0")
        return
    # Unity batchmode 退出后文件锁可能短暂残留
    last_err: Exception | None = None
    for attempt in range(5):
        try:
            path.write_text(text, encoding="utf-8", newline="\n")
            log("reset VersionRecord -> 1.0.0 / Build 0")
            return
        except OSError as exc:
            last_err = exc
            time.sleep(0.4 * (attempt + 1))
    raise OSError(f"reset VersionRecord failed after retries: {last_err}") from last_err


def reset_fixtures(dry_run: bool) -> None:
    write_text(
        ROOT / "Assets/Test/FYAssetPipeline/FYAssetPipelineSync.txt",
        "fyasset-pipeline-sync:v1\n",
        dry_run,
    )
    write_text(
        ROOT / "Assets/Test/FYAssetPipeline/FYAssetPipelineRaw.fyraw",
        "fyasset-pipeline-raw:v1\n",
        dry_run,
    )


def reset_streaming_assets(dry_run: bool) -> None:
    sa = ROOT / "Assets" / "StreamingAssets"
    names = [
        "BuildIndex.json",
        "AAManifest.json",
        "AAManifest.bin",
        "ABManifest.json",
        "ABManifest.bin",
        "catalog.json",
        "catalog.hash",
        "catalog.bundle",
    ]
    for name in names:
        rm_path(sa / name, dry_run)
    # Unity often creates .meta siblings for exported files
    for name in names:
        rm_path(sa / f"{name}.meta", dry_run)
    bundles = sa / "bundles"
    if bundles.exists():
        clear_dir_contents(bundles, dry_run)
    # Standalone offline package isolation directory
    standalone = sa / "Standalone"
    if standalone.exists():
        clear_dir_contents(standalone, dry_run)
        rm_path(standalone, dry_run)
    rm_path(sa / "Standalone.meta", dry_run)


def reset_backend(backend: str, dry_run: bool, reports: bool) -> None:
    backend = backend.upper()
    # Repository snapshots for all platforms under BuildData/Snapshots
    snapshots = ROOT / "BuildData" / "Snapshots"
    if snapshots.exists():
        for platform_dir in snapshots.iterdir():
            if not platform_dir.is_dir():
                continue
            rm_path(platform_dir / backend, dry_run)

    if reports:
        rm_path(ROOT / "BuildData" / "Reports" / backend, dry_run)

    # Local publish service root backend folder
    local_root = ROOT / "HotfixPublish" / "Local" / backend
    if local_root.exists():
        clear_dir_contents(local_root, dry_run)
        # keep directory itself
        if not dry_run:
            local_root.mkdir(parents=True, exist_ok=True)


def reset_shared_outputs(dry_run: bool, keep_testruns: bool, clear_publish: bool) -> None:
    packages = ROOT / "HotfixOutput" / "Packages"
    if packages.exists():
        clear_dir_contents(packages, dry_run)
    rm_path(ROOT / "HotfixOutput" / "PackageIndex.json", dry_run)
    rm_path(ROOT / "HotfixOutput" / "build.log", dry_run)

    if not keep_testruns:
        rm_path(ROOT / "HotfixOutput" / "TestRuns", dry_run)

    bootstrap = ROOT / "Assets" / "Build" / "Bootstrap" / "BuildIndex.json"
    if bootstrap.exists():
        # leave empty object or delete; deleting is cleaner for "no baseline"
        rm_path(bootstrap, dry_run)
        rm_path(bootstrap.with_suffix(".json.meta"), dry_run)

    # AA pending hotfix group moves
    undo = ROOT / "Assets" / "FYAsset" / "Editor" / "Generated" / "HotfixGroupUndoLog.json"
    write_text(undo, json.dumps({"Entries": []}, indent=4) + "\n", dry_run)

    if clear_publish:
        for name in ("Local", "Cloudflare"):
            root = ROOT / "HotfixPublish" / name
            if not root.exists():
                continue
            for child in root.iterdir():
                # keep Cloudflare _headers if present
                if child.name == "_headers":
                    continue
                rm_path(child, dry_run)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    p = argparse.ArgumentParser(prog="fyasset-reset")
    p.add_argument(
        "scope",
        choices=["aa", "ab", "all"],
        help="Which backend repository/report state to clear",
    )
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--keep-testruns", action="store_true", help="Keep HotfixOutput/TestRuns evidence")
    p.add_argument("--reports", action="store_true", help="Also delete BuildData/Reports/{AA|AB}")
    p.add_argument(
        "--clear-publish",
        action="store_true",
        help="Also clear HotfixPublish/Local and Cloudflare service trees (keeps _headers)",
    )
    p.add_argument(
        "--no-fixtures",
        action="store_true",
        help="Do not rewrite permanent pipeline fixture markers to v1",
    )
    return p.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    log(f"scope={args.scope} dry_run={args.dry_run}")

    if args.scope in ("aa", "all"):
        reset_backend("AA", args.dry_run, args.reports)
    if args.scope in ("ab", "all"):
        reset_backend("AB", args.dry_run, args.reports)

    # Shared package/output state is always cleaned for practical "full clean"
    # even when scope is aa/ab, because PackageIndex/Packages are shared roots.
    reset_shared_outputs(args.dry_run, args.keep_testruns, args.clear_publish)
    reset_streaming_assets(args.dry_run)
    reset_version_database(args.dry_run)
    if not args.no_fixtures:
        reset_fixtures(args.dry_run)

    log("done")
    log("Note: if Unity Editor is open, refresh/reimport after reset.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
