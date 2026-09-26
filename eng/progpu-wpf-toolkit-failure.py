#!/usr/bin/env python3
"""Record failed Toolkit probe diagnostics without changing validation outcome."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def record_failure(directory, exit_code, process_id):
    """The caller owns this fresh probe directory and still owns the live child."""
    directory = Path(directory).resolve(strict=True)
    report = {
        "schemaVersion": 1,
        "validationExitCode": exit_code,
        "processId": process_id,
        "screenshot": "disabled",
    }
    # Never capture a developer's desktop from an ambient CI-like flag alone.
    enabled = os.environ.get("PROGPU_WPF_TOOLKIT_FAILURE_SCREENSHOT") == "1"
    hosted = (sys.platform == "darwin"
              and os.environ.get("GITHUB_ACTIONS") == "true"
              and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted")
    # Exclusive creation also prevents a repeated diagnostic call from replacing
    # the original report or screenshot in an existing probe directory.
    with (directory / "failure.json").open("x", encoding="utf-8") as receipt:
        if enabled and hosted:
            screenshot = directory / "failure.png"
            if screenshot.exists():
                report["screenshot"] = "existing-file-not-overwritten"
            else:
                try:
                    with (directory / "screenshot.log").open("xb") as output:
                        result = subprocess.run(
                            ["/usr/sbin/screencapture", "-x", "-m", str(screenshot)],
                            stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT,
                            timeout=3, check=False,
                        )
                    report["screenshotExitCode"] = result.returncode
                    report["screenshot"] = (
                        "captured" if result.returncode == 0 and screenshot.is_file()
                        and screenshot.stat().st_size > 0 else "failed"
                    )
                except subprocess.TimeoutExpired:
                    report["screenshot"] = "timed-out"
                except OSError as error:
                    report["screenshot"] = "unavailable"
                    report["screenshotError"] = str(error)
        elif enabled:
            report["screenshot"] = "not-hosted-macos"

        json.dump(report, receipt, indent=2)
        receipt.write("\n")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--exit-code", type=int, required=True)
    parser.add_argument("--process-id", type=int, required=True)
    args = parser.parse_args()
    if args.exit_code == 0 or args.process_id < 0:
        parser.error("Expected a failing validation exit code and a nonnegative owned process ID.")
    record_failure(args.directory, args.exit_code, args.process_id)


if __name__ == "__main__":
    main()
