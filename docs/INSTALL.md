# Installing AirType

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11.
- x64 processor and operating system.
- Microphone permission for dictation.
- Internet access for cloud providers and optional Local ASR downloads.

The supported ZIP is self-contained. Users do not need to install .NET, the
.NET Desktop Runtime, Windows App SDK, or Python separately.

## Choose An Artifact

Official artifacts are published only on the
[AirType GitHub Releases page](https://github.com/samonjoat/AirType/releases).

- `AirType-<version>-win-x64.zip` is a signed stable package.
- `AirType-<version>-win-x64-unsigned.zip` is an unsigned validation candidate.

Unsigned candidates are for release testing. They are not supported stable
releases and may trigger stronger Windows warnings. Do not obtain AirType from
third-party download sites.

## Verify The Download

Each ZIP has a matching `.sha256` sidecar. In PowerShell:

```powershell
$zip = '.\AirType-1.0.0-win-x64.zip'
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content ([IO.Path]::ChangeExtension($zip, '.sha256'))
```

The first value in the sidecar must equal the calculated hash. For a stable
release, open `AirType.exe` properties after extraction, select **Digital
Signatures**, and confirm Windows reports a valid signature whose publisher
matches the release notes. Stop if the stable artifact is unsigned, invalid,
or has a different checksum. See the project
[Code signing policy](../CODE_SIGNING_POLICY.md) for signing scope and controls.

## Install And Start

1. Extract the entire ZIP to a user-writable folder such as
   `%LOCALAPPDATA%\Programs\AirType`. Do not run the executable inside the ZIP.
2. Run `AirType.exe`.
3. Grant microphone permission when Windows requests it.
4. Open Settings and choose an input device.
5. Configure Local ASR or add your own Gemini, Groq, or OpenRouter API key.
6. Set and test the global recording hotkey.

Local ASR installation downloads the versioned runtime and `small.en` model
bundle from the official AirType GitHub Release. AirType verifies the manifest
and SHA-256 checksum before installing it. The download can take several minutes
and uses substantial disk space; keep AirType open until setup finishes. Cloud
providers can impose their own account requirements, rate limits, retention,
and charges.

## Application Data

AirType stores user data under `%LOCALAPPDATA%\AirType\`, separate from the
application folder. This includes settings, encrypted provider credentials,
recordings, transcript history, notes, dictionary entries, and logs. See
[`../PRIVACY.md`](../PRIVACY.md) for retention and deletion details.

## Update

1. Close AirType from the tray and confirm it is no longer running.
2. Download and verify the new ZIP and sidecar.
3. Back up `%LOCALAPPDATA%\AirType\` before a major update.
4. Extract the new package and replace the previous application files.
5. Start AirType and confirm Settings and History load correctly.

Do not mix files from different AirType versions in one application directory.

## Uninstall

Close AirType and delete the extracted application folder. To also delete local
recordings, transcripts, notes, settings, credentials, installed Local ASR
files, and logs, delete `%LOCALAPPDATA%\AirType\` while AirType is closed.

Removing local files does not delete data retained by cloud providers.

## Troubleshooting

- A .NET Desktop Runtime prompt indicates an incomplete or non-self-contained package.
- Missing adjacent DLL or runtime errors usually mean the ZIP was not fully extracted.
- A disabled Local ASR download button means the release has no valid install source.
- A Local ASR 404 before launch publication is expected because private GitHub
  Release assets are unavailable to AirType's unauthenticated downloader.
- Hotkey registration can fail when another application already owns that key combination.
- Report reproducible defects through GitHub Issues without attaching real recordings,
  transcripts, credentials, or trace files.
