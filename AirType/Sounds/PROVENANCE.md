# Notification Sound Provenance

AirType's Start, Done, and Cancel notification sounds are deterministic,
first-party generated assets. They were synthesized by
`tools/generate-notification-sound-candidates.py`; no stock-audio or converted
third-party source files are used by the production application.

## Approved Selection

| Event | Candidate | Duration | SHA-256 |
| --- | --- | ---: | --- |
| Start | Pulse Low | 151 ms | `7d8b5a74ee54a7a5d2b93547ad6682de3bf2cd2a002208d7bea24525ec94eec0` |
| Done | Pulse Warm | 190 ms | `b55bf39e4ea7aa0a0eea6c359a6deca04fdea8d814f6ecf70c834aee92a8621e` |
| Cancel | Pulse Warm | 198 ms | `7bfa7b456e3213a76a2a8775f07c58565811b0faba93be20d7f678e3e8f68fae` |

All files are mono 44.1 kHz 16-bit PCM WAV files. The Done asset is amplified
at playback by AirType's existing event gain; its stored waveform remains at a
conservative peak level.

## Verification

Install Python 3.10 or later and NumPy, then run:

```powershell
python .\tools\generate-notification-sound-candidates.py --verify-production
```

The command regenerates the selected cues in memory, verifies their expected
hashes, and compares them byte-for-byte with `AirType/Sounds` without modifying
the production files.
