# Offline announcements

These small 22.05 kHz mono PCM WAV recordings are embedded in the application. Default speech reads the embedded resources directly through a persistent audio output, without a beep. No model, speech service, network connection, Python, or ONNX engine is required on the user's PC.

- All eight clips are the publisher's own supplied recordings, preserved in full and level-balanced at their explicit request. Original recording containers are not distributed.
- No generated or neural voice clips remain in this pack. There are no voice models, generators, or cloud dependencies in the application.

`manifest.json` records each phrase and its source. New recordings can replace the matching WAV without changing the app's network behavior. Existing Windows voice choices remain available as alternatives.

`scripts/import-recordings.py` converts named CAF/WAV/M4A/MP3 files from the ignored `sounds/` folder. It uses a development-only FFmpeg decoder and writes standard PCM resources; neither FFmpeg nor Python is included in NetOn.
