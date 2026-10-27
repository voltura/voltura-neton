"""Development helper: convert the publisher's recordings into embedded offline clips.

Requires numpy, soundfile, and imageio-ffmpeg. Original files are never modified.
"""
from pathlib import Path
import argparse
import json
import subprocess
import tempfile
import os
import shutil
import imageio_ffmpeg
import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, default=ROOT / "sounds")
parser.add_argument("--output", type=Path, default=ROOT / "assets/voice")
args = parser.parse_args()
phrases = {
    "internet_is_on": ("internet-on", "Internet is on."),
    "internet_is_off": ("internet-off", "Internet is off."),
    "neton_will_start_with_windows": ("startup-on", "NetOn will start with Windows."),
    "neton_will_not_start_with_windows": ("startup-off", "NetOn will not start with Windows."),
    "colors_reset": ("colors-reset", "Colors reset."),
    "please_select_a_foreground_color": ("foreground", "Please select a foreground color."),
    "please_select_foreground_color": ("foreground", "Please select a foreground color."),
    "please_select_a_background_color": ("background", "Please select a background color."),
    "please_select_background_color": ("background", "Please select a background color."),
    "voltura_ab": ("about", "Voltura AB"),
    "neton_by_voltura_ab": ("about", "NetOn by Voltura AB"),
}
manifest_path = args.output / "manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else []
templates = {entry["id"]: entry for entry in json.loads((ROOT / "assets/voice/manifest.json").read_text(encoding="utf-8"))}
sources = sorted(args.source.iterdir())
supported = {".caf", ".wav", ".m4a", ".mp3"}
has_full_brand = any(source.stem.lower().replace(" ", "_") == "neton_by_voltura_ab" and source.suffix.lower() in supported for source in sources)
args.output.mkdir(parents=True, exist_ok=True)
messages = []
with tempfile.TemporaryDirectory(prefix=".neton-import-", dir=args.output) as temporary:
    staged = Path(temporary)
    outputs = {}
    for source in sources:
        key = source.stem.lower().replace(" ", "_")
        if key not in phrases or source.suffix.lower() not in supported:
            continue
        if key == "voltura_ab" and has_full_brand:
            continue  # Prefer the full product introduction over the company-only clip.
        identifier, spoken = phrases[key]
        decoded = staged / "decoded.wav"
        subprocess.run([
            imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", str(source),
            "-ar", "22050", "-ac", "1", "-c:a", "pcm_s16le", "-y", str(decoded),
        ], check=True)
        samples, rate = sf.read(decoded, dtype="float32")
        # Keep every decoded sample, including natural pauses and quiet consonants.
        clip = samples
        peak = float(np.max(np.abs(clip)))
        if peak:
            clip *= .88 / peak
        wave = staged / (identifier + ".wav")
        sf.write(wave, clip, rate, subtype="PCM_16")
        outputs[args.output / wave.name] = wave
        entry = next((entry for entry in manifest if entry["id"] == identifier), None)
        if entry is None:
            entry = dict(templates[identifier])
            manifest.append(entry)
        entry.update(voice="user-provided recording", spoken=spoken, phonemes=None, speed=1, sampleRate=rate, duration=round(len(clip) / rate, 3))
        messages.append(f"{source.name} -> {identifier}.wav ({len(clip) / rate:.2f}s)")
    staged_manifest = staged / "manifest.json"
    staged_manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    outputs[manifest_path] = staged_manifest
    # Decode and write everything before touching the existing recordings. Keep
    # backups until the manifest and all its clips have been committed together.
    backups = {}
    for index, target in enumerate(outputs):
        backup = staged / f"backup-{index}"
        if target.exists():
            shutil.copy2(target, backup)
            backups[target] = backup
        else:
            backups[target] = None
    committed = []
    try:
        for target, replacement in outputs.items():
            os.replace(replacement, target)
            committed.append(target)
    except BaseException:
        for target in reversed(committed):
            backup = backups[target]
            if backup is None:
                target.unlink()
            else:
                os.replace(backup, target)
        raise
for message in messages:
    print(message)
