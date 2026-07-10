# AirType Release Procedure

This maintainer procedure produces the portable Windows x64 package and Local
ASR release assets. An unsigned validation candidate is never a stable release.

## Release Requirements

- A clean commit on the public repository's `main` branch.
- The .NET SDK pinned by `global.json` on Windows x64.
- PowerShell, Python 3.12, and network access for dependency preparation/audits.
- Passing GitHub Actions build, test, dependency, secret, and snapshot checks.
- Passing Windows 10 and Windows 11 clean-machine acceptance for the exact artifact.
- A trusted Authenticode signature for a stable release.

AirType intends to use SignPath Foundation's free open-source signing program.
No paid certificate is required by this procedure. If trusted signing is not
available, the artifact remains an explicitly unsigned release candidate.

## Validate Source

Run from the repository root:

```powershell
dotnet clean .\AirType\AirType.csproj
dotnet build .\AirType\AirType.csproj
dotnet test .\AirType.Tests\AirType.Tests.csproj --no-restore

Set-Location .\AirType.LocalAsrWorker
python -m pytest .\tests -q
Set-Location ..

dotnet list .\AirType\AirType.csproj package --vulnerable --include-transitive
python -m pip_audit -r .\AirType.LocalAsrWorker\requirements-runtime.txt
python .\tools\generate-notification-sound-candidates.py --verify-production
```

Generate the allowlisted public tree and run its clean-clone checks:

```powershell
.\tools\build-public-snapshot.ps1
```

The snapshot must not contain private history, developer paths, credentials,
databases, logs, recordings, internal plans, or generated build/runtime output.

## Build An Unsigned Validation Candidate

From a clean commit, run:

```powershell
.\tools\build-release.ps1 -Version 1.0.0
```

The output name contains `-unsigned`. The script publishes self-contained,
collects exact third-party license evidence, writes release provenance and
checksums, and validates the ZIP in an isolated temporary directory.

Verify it independently:

```powershell
.\tools\verify-release-package.ps1 `
  -PackagePath .\release-artifacts\app\AirType-1.0.0-win-x64-unsigned.zip `
  -AllowUnsigned
```

Use this candidate for clean-machine testing and, when required, SignPath
Foundation project review. Do not label it stable.

Build and independently verify the matching Local ASR assets:

```powershell
.\tools\package-local-asr-bundle.ps1 -Version 1.0.0 -PrepareRuntime -Force
.\tools\verify-local-asr-bundle.ps1 `
  -ManifestPath .\release-artifacts\local-asr\airtype-local-asr-small-en-ct2-win-x64-v1.0.0.json
```

The verifier checks the sidecar and ZIP hashes, deterministic archive metadata,
safe extraction paths, source provenance, license inventory, portable CPython,
the pinned Microsoft-signed app-local Visual C++ runtime, PCM-only compatibility
shim, model files, and imports from the extracted runtime.
Before the repository is public, test the app's install path by setting
`AIRTYPE_LOCAL_ENGINE_BUNDLE_URL` to a local verified manifest or ZIP. The
committed source points to the final GitHub Release URL, which returns 404 while
the repository and release remain private.

Run the candidate inside each clean Windows VM and retain the non-sensitive JSON
result:

```powershell
.\tools\verify-clean-windows-candidate.ps1 `
  -AppPackagePath <app-zip> `
  -ExpectedAppSha256 <app-sha256> `
  -LocalAsrPackagePath <local-asr-zip> `
  -ExpectedLocalAsrSha256 <local-asr-sha256> `
  -OutputPath <evidence-json>
```

The `1.0.0` private candidate results and environment details are recorded in
[`release-acceptance/1.0.0`](release-acceptance/1.0.0/README.md).

## Clean-Machine Acceptance

Test only the ZIP and checksum sidecar, never a developer build folder. On
clean Windows 10 x64 and Windows 11 x64 systems without .NET, Windows App SDK,
or Python installed:

1. Verify the checksum and expected signature state.
2. Extract and launch against a new Windows profile.
3. Restart and confirm Settings, History, Notes, and Dictionary persist.
4. Install, use, remove, and reinstall Local ASR.
5. Run cloud ASR and cleanup with dedicated test credentials.
6. Verify hotkey, tray, startup, recording, cancellation, transcript insertion,
   history rerun/source toggle/edit/delete, audio download, and Clear History.
7. Verify light/dark themes, minimum size, and primary/secondary monitor window behavior.
8. Confirm storage remains under `%LOCALAPPDATA%\AirType\` and uninstall cleanly.

Record the OS build, source commit, artifact hash, signature state, and result.

## Produce A Signed Stable Package

For local certificate signing, set the protected certificate thumbprint and run:

```powershell
$env:AIRTYPE_SIGNING_CERTIFICATE_THUMBPRINT = '<protected thumbprint>'
.\tools\build-release.ps1 -Version 1.0.0 -PublicRelease
```

The GitHub release workflow may instead submit the verified executable to
SignPath and assemble the stable package from the returned signed artifact. In
either path, the signed executable must correspond to the tested source and
release manifest. Any rebuild or byte change requires package verification and
clean-machine acceptance again.

## Publish

Create a GitHub Release from the exact tested `main` commit. Upload the signed
ZIP, checksum, Local ASR manifest/bundle/checksums, release notes, license,
privacy notice, installation guide, and generated third-party inventory.

After upload, verify all assets from GitHub, test Local ASR installation through
the public URL, and only then mark the release stable. The Local ASR tag must be
`local-asr-small-en-ct2-v<version>` and its three asset filenames must exactly
match the generated ZIP, JSON, and SHA-256 sidecar. Keep the repository private
until source, workflows, and private fresh-clone validation pass; change
visibility only with explicit maintainer approval.
