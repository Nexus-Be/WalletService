#!/usr/bin/env python3
import json
import os
from pathlib import Path
import subprocess
import unittest


ROOT = Path(__file__).resolve().parent.parent
CHECK = ROOT / ".specify/scripts/bash/check-prerequisites.sh"
FEATURE_STATE = ROOT / ".specify/feature.json"


class FeaturePathTests(unittest.TestCase):
    def run_check(self, environment):
        return subprocess.run(
            [str(CHECK), "--paths-only", "--json"],
            cwd=ROOT,
            env=environment,
            text=True,
            capture_output=True,
            check=False,
        )

    def test_explicit_path_is_data_and_does_not_change_feature_state(self):
        previous = FEATURE_STATE.read_bytes() if FEATURE_STATE.exists() else None
        environment = os.environ.copy()
        environment["SPECIFY_FEATURE_DIRECTORY"] = "specs/a path & 'quoted' $name"
        environment.pop("SPECIFY_INIT_DIR", None)
        result = self.run_check(environment)
        self.assertEqual(0, result.returncode, result.stderr)
        paths = json.loads(result.stdout)
        expected = ROOT / environment["SPECIFY_FEATURE_DIRECTORY"]
        self.assertEqual(str(expected), paths["FEATURE_DIR"])
        self.assertEqual(str(expected / "spec.md"), paths["FEATURE_SPEC"])
        self.assertEqual(str(expected / "plan.md"), paths["IMPL_PLAN"])
        self.assertEqual(previous, FEATURE_STATE.read_bytes() if FEATURE_STATE.exists() else None)

    def test_invalid_project_override_fails(self):
        environment = os.environ.copy()
        environment["SPECIFY_INIT_DIR"] = str(ROOT / "nonexistent-specify-test-directory")
        result = self.run_check(environment)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("SPECIFY_INIT_DIR", result.stderr)


if __name__ == "__main__":
    unittest.main()
