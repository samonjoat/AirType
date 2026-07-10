# AirType Architecture

## Runtime Shape

AirType is a .NET 8 WPF desktop application using MVVM and an explicit singleton
service container. It runs as one user-level Windows process. Local ASR runs in
a child Python worker process over a localhost WebSocket protocol.

No AirType-operated backend is required. Cloud transcription and cleanup calls
go directly from the desktop application to the provider configured by the user.

## Main Projects

- `AirType`: views, view models, models, Windows integration, persistence, and workflows.
- `AirType.LocalAsrWorker`: faster-whisper process and binary PCM protocol.
- `AirType.Tests`: xUnit behavior, storage, source-contract, packaging, and regression tests.
- `tools`: portable-runtime preparation, dependency inventory, packaging, and verification.

## Transcription Flow

1. `HotkeyManager` or the application UI starts `AudioInputManager` recording.
2. `TranscriptionWorkflowService` closes the recording session and selects Local,
   Gemini, Groq, or OpenRouter ASR according to current settings and availability.
3. Local ASR streams normalized PCM to the worker. Cloud ASR sends encoded audio
   and request guidance directly to the selected provider.
4. The raw ASR transcript is retained. Optional cleanup sends that text plus the
   active context, formatting, prompt, and dictionary guidance to a cleanup provider.
5. `HistoryDatabase` persists session identity, raw/final text, provider/model,
   timing, audio path, and the per-session display-source preference.
6. `TextInjectionService` verifies the target and chooses the safest available
   UI Automation, direct control, clipboard paste, native typing, or clipboard-only path.

Every session uses one `Guid` across its database row, WAV file, and technical
transcription trace.

## Storage

User data is rooted under `%LOCALAPPDATA%\AirType\` through
`AirTypeStoragePaths`. SQLite stores history, dictionary, notes, and prompts.
Provider credentials are protected for the current Windows user with DPAPI.
Recordings, logs, and provider traces remain ordinary local files; see
`PRIVACY.md` for retention and deletion behavior.

Tests and release smoke checks override the storage root so they cannot write to
the developer's or user's real profile.

## Local ASR

Release packages include a portable CPython runtime and worker dependencies but
do not require machine-installed Python. The speech model is installed on demand
from a verified manifest/bundle or its documented upstream model source.

The worker accepts only localhost connections using an application-generated
session token. It consumes normalized PCM supplied by AirType and does not need
PyAV or a separately installed FFmpeg runtime.

## Themes And Views

The UI uses WPF resource dictionaries for light/dark themes and shared control
styles. Views contain layout and interaction wiring; view models own observable
state and commands; services own persistence, provider access, audio, and native
Windows mechanics.

## Release Trust Boundary

`tools/build-release.ps1` creates a self-contained x64 artifact, exact dependency
license inventory, release manifest, deterministic ZIP, and SHA-256 sidecar.
`tools/verify-release-package.ps1` extracts into isolation and verifies runtime
completeness, architecture, signature state, developer-path absence, Local ASR
portability, and fresh-profile startup.

The public repository is generated from an explicit allowlist. It never inherits
the private development repository's Git objects or internal artifacts.
