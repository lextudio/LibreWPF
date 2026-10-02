#!/usr/bin/env python3
"""Execute the canonical workflow staging step with a recording helper, offline."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/progpu-wpf-sdk.yml"


class CanonicalNativeStagingTests(unittest.TestCase):
    def staging_step(self):
        workflow = WORKFLOW.read_text()
        canonical = workflow.split("\n  canonical-winforms-integration:\n", 1)[1].split("\n  portable-source-contracts:\n", 1)[0]
        name = "      - name: Stage exact native drawing package dependency\n"
        self.assertLess(canonical.index(name), canonical.index("      - name: Build canonical WinForms and WindowsFormsIntegration\n"))
        step = canonical.split(name, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def run_step(self, helper_status=0, git_status=0):
        with tempfile.TemporaryDirectory(prefix="wpf-canonical-staging-") as temporary:
            root = Path(temporary)
            (root / "eng").mkdir()
            helper = root / "eng/progpu-stage-ci-native-runtimes.sh"
            # This stub records arguments only; it cannot create native payloads
            # and is never used by package production or the real staging helper.
            helper.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$STAGING_RECEIPT"\nexit "$STAGING_STATUS"\n')
            helper.chmod(0o755)
            receipt = root / "arguments"
            environment = dict(os.environ, GITHUB_WORKSPACE=str(root), STAGING_RECEIPT=str(receipt),
                               STAGING_STATUS=str(helper_status), GIT_STATUS=str(git_status))
            script = '''git() {
  test "$*" = "-C external/ProGPU rev-parse HEAD" || return 91
  test "$GIT_STATUS" = 0 || return "$GIT_STATUS"
  printf '%s\\n' aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
}
'''+ self.staging_step()
            result = subprocess.run(["bash", "-c", script], cwd=root, env=environment, capture_output=True, text=True)
            return result.returncode, receipt.read_text().splitlines() if receipt.exists() else [], str(root)

    def test_stages_exact_checkout_into_its_native_package_root(self):
        status, arguments, root = self.run_step()
        self.assertEqual(status, 0)
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package"])

    def test_failed_producer_staging_fails_the_step(self):
        status, arguments, _ = self.run_step(helper_status=23)
        self.assertEqual(status, 23)
        self.assertEqual(arguments[0], "a" * 40)

    def test_unresolved_source_identity_never_invokes_staging(self):
        status, arguments, _ = self.run_step(git_status=27)
        self.assertEqual(status, 27)
        self.assertEqual(arguments, [])


if __name__ == "__main__":
    unittest.main()
