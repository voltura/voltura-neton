"""Development helper: convert the publisher's recordings into embedded offline clips.

Requires numpy, soundfile, and imageio-ffmpeg. Original files are never modified.
"""
from pathlib import Path
import argparse
import json
import subprocess
import tempfile
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
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
for source in sorted(args.source.iterdir()):
    key = source.stem.lower().replace(" ", "_")
    if key not in phrases or source.suffix.lower() not in {".caf", ".wav", ".m4a", ".mp3"}:
        continue
    identifier, spoken = phrases[key]
    with tempfile.TemporaryDirectory(prefix="neton-recording-") as temporary:
        decoded = Path(temporary) / "decoded.wav"
        subprocess.run([
            imageio_ffmpeg.get_ffmpeg_exe(), "-v", "error", "-i", str(source),
            "-ar", "22050", "-ac", "1", "-c:a", "pcm_s16le", "-y", str(decoded),
        ], check=True)
        samples, rate = sf.read(decoded, dtype="float32")
    # Keep every decoded sample, including natural pauses and quiet consonants.
    # Opening/closing the output is handled separately by the playback engine.
    clip = samples
    peak = float(np.max(np.abs(clip)))
    if peak:
        clip *= .88 / peak
    sf.write(args.output / (identifier + ".wav"), clip, rate, subtype="PCM_16")
    for entry in manifest:
        if entry["id"] == identifier:
            entry.update(voice="user-provided recording", spoken=spoken, phonemes=None, speed=1, duration=round(len(clip) / rate, 3))
    print(f"{source.name} -> {identifier}.wav ({len(clip) / rate:.2f}s)")
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
