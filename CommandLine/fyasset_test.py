#!/usr/bin/env python3
"""fyasset-test CLI: aa|ab build|e2e full|hotfix|chain --target <id> [...]"""

from __future__ import annotations

import argparse
import os
import signal
import subprocess
import sys
import time
from pathlib import Path


EXIT_INVALID = 2
EXIT_PRECONDITION = 3
EXIT_INTERRUPTED = 130


def project_root() -> Path:
    return Path(__file__).resolve().parent.parent


def load_unity_path(root: Path) -> str:
    config = root / "CommandLine" / "build.config"
    if not config.exists():
        raise SystemExit(f"Missing {config}")
    for line in config.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line.startswith("UnityPath="):
            value = line.split("=", 1)[1].strip()
            if value:
                return value
    raise SystemExit("UnityPath not set in CommandLine/build.config")


def editor_running() -> bool:
    if os.name == "nt":
        try:
            out = subprocess.check_output(
                ["tasklist", "/FI", "IMAGENAME eq Unity.exe"],
                stderr=subprocess.DEVNULL,
                text=True,
                encoding="utf-8",
                errors="ignore",
            )
            return "Unity.exe" in out
        except Exception:
            return False
    try:
        out = subprocess.check_output(["pgrep", "-x", "Unity"], text=True)
        return bool(out.strip())
    except Exception:
        return False


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="fyasset-test")
    sub = p.add_subparsers(dest="backend", required=True)

    for backend in ("aa", "ab"):
        bp = sub.add_parser(backend)
        kind = bp.add_subparsers(dest="kind", required=True)
        for k in ("build", "e2e"):
            kp = kind.add_parser(k)
            kp.add_argument("mode", choices=["full", "hotfix", "chain", "standalone"])
            kp.add_argument(
                "--target",
                action="append",
                dest="targets",
                required=False,
                default=[],
                help="Explicit Push Target id (repeatable); not required for standalone mode",
            )
            kp.add_argument(
                "--confirm-external-publish",
                action="append",
                dest="external_confirms",
                default=[],
                help="Confirm external Target id (repeatable)",
            )
            kp.add_argument("--timeout", type=int, default=0, help="Seconds; 0 = no timeout")
            kp.add_argument("--result-root", default="", help="Optional result directory override")
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    root = project_root()

    if args.kind == "e2e":
        # E2E entry implemented by E2ETestCommandLine.Run.
        method = "E2ETestCommandLine.Run"
    else:
        method = "BuildTestCommandLine.Run"

    # batchmode 退出后 tasklist 可能仍短暂可见 Unity.exe
    for _wait in range(10):
        if not editor_running():
            break
        time.sleep(1.0)
    if editor_running():
        print("[fyasset-test] Unity Editor is open for this project. Close it before batchmode.", file=sys.stderr)
        return EXIT_PRECONDITION

    unity = load_unity_path(root)
    if not Path(unity).exists():
        print(f"[fyasset-test] Unity not found: {unity}", file=sys.stderr)
        return EXIT_INVALID

    log_dir = root / "HotfixOutput" / "TestRuns" / args.backend.upper() / args.kind / args.mode
    log_dir.mkdir(parents=True, exist_ok=True)
    log_file = log_dir / f"cli_{int(time.time())}.log"

    cmd = [
        unity,
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(root),
        "-executeMethod",
        method,
        "-backend",
        args.backend,
        "-mode",
        args.mode,
        "-logFile",
        str(log_file),
    ]
    for t in args.targets or []:
        cmd.extend(["-target", t])
    for c in args.external_confirms or []:
        cmd.extend(["-confirm-external-publish", c])
    if args.result_root:
        cmd.extend(["-resultRoot", args.result_root])

    print("[fyasset-test]", " ".join(cmd))
    print(f"[fyasset-test] log: {log_file}")

    proc = subprocess.Popen(cmd)
    timed_out = False

    def _handle_sigint(signum, frame):
        if proc.poll() is None:
            proc.terminate()
        raise KeyboardInterrupt

    signal.signal(signal.SIGINT, _handle_sigint)
    try:
        if args.timeout and args.timeout > 0:
            try:
                return proc.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                proc.kill()
                proc.wait()
                print("[fyasset-test] timed out", file=sys.stderr)
                return EXIT_PRECONDITION
        return proc.wait()
    except KeyboardInterrupt:
        if proc.poll() is None:
            proc.kill()
            proc.wait()
        print("[fyasset-test] interrupted", file=sys.stderr)
        return EXIT_INTERRUPTED
    finally:
        if timed_out and proc.poll() is None:
            proc.kill()


if __name__ == "__main__":
    sys.exit(main())
