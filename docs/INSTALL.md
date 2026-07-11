# Installing AirType

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11.
- x64 processor and operating system.
- Microphone permission for dictation.
- Internet access for cloud providers and optional Local ASR downloads.

The MSI installer and portable ZIP are self-contained. Users do not need to
install .NET, the .NET Desktop Runtime, Windows App SDK, or Python separately.

## Choose An Artifact

Official artifacts are published only on the
[AirType GitHub Releases page](https://github.com/samonjoat/AirType/releases).

- `AirType-<version>-win-x64.msi` is the signed stable installer.
- `AirType-<version>-win-x64.zip` is the signed stable portable package.
- Names containing `-unsigned` identify unsigned validation candidates in the
  same MSI or ZIP format.

Unsigned candidates are for release testing. They are not supported stable
releases and may trigger stronger Windows warnings. Do not obtain AirType from
third-party download sites.

## Verify The Download

Each artifact has a matching SHA-256 sidecar. The MSI sidecar appends
`.sha256`; the ZIP sidecar replaces `.zip` with `.sha256`. In PowerShell:

```powershell
$zip = '.\AirType-1.0.0-win-x64.zip'
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content ([IO.Path]::ChangeExtension($zip, '.sha256'))

$msi = '.\AirType-1.0.0-win-x64.msi'
(Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content "$msi.sha256"
```

The first value in each sidecar must equal the calculated hash. For a stable
release, open the MSI properties and confirm its **Digital Signatures** tab
reports a valid publisher matching the release notes. The installed or extracted
`AirType.exe` must have the same valid publisher. Stop if either signature is
missing or invalid, or if a checksum differs. See the project
[Code signing policy](../CODE_SIGNING_POLICY.md) for signing scope and controls.

## Install And Start

1. Verify and run `AirType-<version>-win-x64.msi` from an administrator account.
2. Start AirType from the Windows Start menu.
3. Grant microphone permission when Windows requests it.
4. Open Settings and choose an input device.
5. Configure Local ASR or add your own Gemini, Groq, or OpenRouter API key.
6. Set and test the global recording hotkey.

For portable use, extract the entire ZIP to a user-writable folder such as
`%LOCALAPPDATA%\Programs\AirType`, then run `AirType.exe`. Do not run the
executable inside the ZIP and do not mix files from different versions.

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
2. Download and verify the new MSI or portable ZIP and its sidecar.
3. Back up `%LOCALAPPDATA%\AirType\` before a major update.
4. Run the new MSI; it upgrades the existing installed version. For portable
   use, replace the previous application files with the complete new ZIP.
5. Start AirType and confirm Settings and History load correctly.

Do not mix files from different AirType versions in one application directory.

## Uninstall

Close AirType, turn off **Launch on startup** in Settings, and uninstall AirType
from Windows **Installed apps**. Portable users can delete the extracted
application folder. Uninstalling the MSI intentionally preserves recordings,
transcripts, notes, settings, credentials, installed Local ASR files, and logs
under `%LOCALAPPDATA%\AirType\`.

To delete that user data too, delete `%LOCALAPPDATA%\AirType\` while AirType is
closed. Removing local files does not delete data retained by cloud providers.

## Troubleshooting

- A .NET Desktop Runtime prompt indicates an incomplete or non-self-contained package.
- Missing adjacent DLL or runtime errors usually mean the ZIP was not fully extracted.
- MSI installation requires administrator permission and writes application files
  under `%ProgramFiles%\AirType`.
- A disabled Local ASR download button means the release has no valid install source.
- A Local ASR 404 before launch publication is expected because private GitHub
  Release assets are unavailable to AirType's unauthenticated downloader.
- Hotkey registration can fail when another application already owns that key combination.
- Report reproducible defects through GitHub Issues without attaching real recordings,
  transcripts, credentials, or trace files.
