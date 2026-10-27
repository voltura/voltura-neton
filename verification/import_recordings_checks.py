"""Run importer fault cases against temporary files, without audio dependencies."""
from pathlib import Path
from contextlib import redirect_stdout
import io
import json
import os
import runpy
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

IMPORTER = Path(__file__).resolve().parents[1] / "scripts/import-recordings.py"

class Samples:
    def __len__(self): return 22050
    def __imul__(self, other): return self

class ImportChecks(unittest.TestCase):
    def invoke(self, source, output, fail_name=None):
        self.decoded_names = []
        def decode(args, **kwargs):
            name = Path(args[args.index("-i") + 1]).name
            self.decoded_names.append(name)
            if name == fail_name:
                raise subprocess.CalledProcessError(1, "fake decoder")
        modules = {
            "imageio_ffmpeg": types.SimpleNamespace(get_ffmpeg_exe=lambda: "unused"),
            "numpy": types.SimpleNamespace(max=lambda value: .5, abs=lambda value: value),
            "soundfile": types.SimpleNamespace(
                read=lambda *args, **kwargs: (Samples(), 22050),
                write=lambda path, *args, **kwargs: Path(path).write_bytes(b"new recording")),
        }
        with patch.dict(sys.modules, modules), patch.object(subprocess, "run", decode), patch.object(sys, "argv", [str(IMPORTER), "--source", str(source), "--output", str(output)]), redirect_stdout(io.StringIO()):
            runpy.run_path(str(IMPORTER), run_name="__main__")

    def test_new_output_folder(self):
        with tempfile.TemporaryDirectory(prefix="neton-import-new-") as temporary:
            root = Path(temporary)
            source = root / "source"; source.mkdir()
            (source / "Internet is on.caf").write_bytes(b"source")
            output = root / "new" / "output"
            self.invoke(source, output)
            self.assertEqual((output / "internet-on.wav").read_bytes(), b"new recording")
            manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual([entry["id"] for entry in manifest], ["internet-on"])
            self.assertEqual((source / "Internet is on.caf").read_bytes(), b"source")

    def test_decode_failure_preserves_existing_outputs(self):
        with tempfile.TemporaryDirectory(prefix="neton-import-atomic-") as temporary:
            root = Path(temporary)
            source = root / "source"; source.mkdir()
            output = root / "output"; output.mkdir()
            (source / "Colors reset.caf").write_bytes(b"source")
            (source / "Internet is on.caf").write_bytes(b"bad source")
            (output / "colors-reset.wav").write_bytes(b"original recording")
            old_manifest = '[{"id":"colors-reset","duration":9}]'
            (output / "manifest.json").write_text(old_manifest, encoding="utf-8")
            with self.assertRaises(subprocess.CalledProcessError):
                self.invoke(source, output, "Internet is on.caf")
            self.assertEqual((output / "colors-reset.wav").read_bytes(), b"original recording")
            self.assertEqual((output / "manifest.json").read_text(encoding="utf-8"), old_manifest)

    def test_commit_failure_rolls_back_existing_outputs(self):
        with tempfile.TemporaryDirectory(prefix="neton-import-commit-") as temporary:
            root = Path(temporary)
            source = root / "source"; source.mkdir()
            output = root / "output"; output.mkdir()
            (source / "Colors reset.caf").write_bytes(b"source")
            (output / "colors-reset.wav").write_bytes(b"original recording")
            old_manifest = '[{"id":"colors-reset","duration":9}]'
            (output / "manifest.json").write_text(old_manifest, encoding="utf-8")
            replace = os.replace
            def fail_manifest(src, dst):
                if Path(dst) == output / "manifest.json":
                    raise OSError("Simulated manifest commit failure")
                return replace(src, dst)
            with patch.object(os, "replace", fail_manifest), self.assertRaises(OSError):
                self.invoke(source, output)
            self.assertEqual((output / "colors-reset.wav").read_bytes(), b"original recording")
            self.assertEqual((output / "manifest.json").read_text(encoding="utf-8"), old_manifest)

    def test_full_product_recording_still_takes_precedence(self):
        with tempfile.TemporaryDirectory(prefix="neton-import-brand-") as temporary:
            root = Path(temporary)
            source = root / "source"; source.mkdir()
            for name in ("Voltura AB.caf", "NetOn by Voltura AB.caf"):
                (source / name).write_bytes(b"source")
            output = root / "output"
            self.invoke(source, output)
            self.assertEqual(self.decoded_names, ["NetOn by Voltura AB.caf"])
            manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest[0]["spoken"], "NetOn by Voltura AB")

if __name__ == "__main__":
    unittest.main(verbosity=2)
