# AirType

AirType is a Windows desktop application for system-wide voice dictation. It
records from a selected microphone, transcribes locally or through a configured
cloud provider, optionally cleans the transcript, and inserts the result into
the active application.

> **Release status:** AirType is preparing its first public release. There is
> currently no supported stable binary. Files whose names contain `-unsigned`
> are validation candidates, not stable releases.

## Features

- Configurable global recording hotkey and microphone selection.
- Local ASR using faster-whisper with a portable Python runtime.
- Cloud ASR through Gemini, Groq, or OpenRouter using your own credentials.
- Optional transcript cleanup with context, formatting, prompts, and dictionary guidance.
- Context-aware insertion with clipboard-preserving fallbacks.
- Local history containing audio, raw ASR text, cleaner text, duration, and provider metadata.
- Per-session ASR/cleaner transcript switching and history reruns.
- Vocabulary, correction dictionary, notes, prompts, statistics, tray, and startup controls.
- Light and dark themes with a compact recording capsule.

## Data And Privacy

AirType does not require an AirType account and does not send product telemetry
to an AirType-operated server. Recordings, transcripts, notes, settings, logs,
and provider traces are stored under `%LOCALAPPDATA%\AirType\`.

Cloud ASR and cleanup send audio or transcript content to the provider selected
in Settings. Local ASR keeps transcription on the computer, although model or
runtime installation requires downloading release/model files. Read
[`PRIVACY.md`](PRIVACY.md) before using sensitive recordings.

## Installation

The primary Windows package is a self-contained x64 MSI installer. A matching
self-contained ZIP is available for portable use. Neither distribution requires
a separate .NET Desktop Runtime, Windows App SDK, or Python installation.

When a stable release is available, download it only from
[GitHub Releases](https://github.com/samonjoat/AirType/releases), verify its
SHA-256 sidecar and Authenticode signature, then run the MSI or extract the
entire portable ZIP. See [`docs/INSTALL.md`](docs/INSTALL.md) for the complete
process.

## Building From Source

Requirements:

- Windows 10 build 19041 or later, or Windows 11, on x64.
- The .NET 8 SDK selected by [`global.json`](global.json).
- PowerShell and Git.
- Python 3.12 for worker tests or notification-sound regeneration.

From the repository root:

```powershell
dotnet clean .\AirType\AirType.csproj
dotnet build .\AirType\AirType.csproj
.\tools\test.ps1 -NoRestore
dotnet run --project .\AirType\AirType.csproj
```

Run Local ASR worker tests:

```powershell
Set-Location .\AirType.LocalAsrWorker
python -m pip install -e ".[dev]"
python -m pytest .\tests -q
```

Prepare the portable Local ASR runtime used by release builds:

```powershell
.\tools\prepare-local-asr-runtime.ps1 -SkipModel
```

This downloads pinned CPython, Python wheels, and Microsoft Visual C++ runtime
components into ignored build directories. The script uses a pinned WiX build
tool to extract Microsoft-signed app-local runtime files; it does not install
Python or the Visual C++ Redistributable globally.

## Repository Layout

- `AirType/`: WPF application and first-party production assets.
- `AirType.LocalAsrWorker/`: faster-whisper worker and Python tests.
- `AirType.Tests/`: xUnit application and packaging tests.
- `docs/`: installation, architecture, privacy-adjacent, and release guidance.
- `tools/`: deterministic build, audit, packaging, and asset-generation scripts.
- `.github/`: continuous-integration, security, and release workflows.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for service boundaries and
the transcription data flow.

## Code Signing Policy

Official stable packages follow the [AirType Code Signing Policy](CODE_SIGNING_POLICY.md).
Free code signing is provided by SignPath.io, with the certificate provided by
SignPath Foundation. Unsigned validation candidates are never stable releases.

## Contributing And Security

Read [`CONTRIBUTING.md`](CONTRIBUTING.md) and sign every contribution under the
Developer's Certificate of Origin. Follow [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).
Report vulnerabilities privately as described in [`SECURITY.md`](SECURITY.md).

## License And Branding

First-party source and reproducible first-party assets are licensed under
[`AGPL-3.0-only`](LICENSE). Third-party components retain their own licenses as
documented in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) and each release's
generated dependency inventory.

The source license permits commercial use subject to its terms. It does not
grant rights to the AirType name, logo, or official-release identity. Modified
distributions must follow [`TRADEMARKS.md`](TRADEMARKS.md).
