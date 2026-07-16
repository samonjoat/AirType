# AirType Release Procedure

This maintainer procedure produces the Windows x64 MSI installer, matching
portable ZIP, and Local ASR assets. Current AirType Windows releases are
explicitly unsigned and retain `-unsigned` in their filenames.

## Release Requirements

- A clean commit from the public repository with protected-branch checks passed.
- The .NET SDK pinned by `global.json` on Windows x64.
- PowerShell, Python 3.12, and network access for dependency preparation/audits.
- Passing build, test, dependency, secret, CodeQL, and public-snapshot checks.
- Passing Windows 10 and Windows 11 clean-machine acceptance for the exact
  package bytes, or an explicit exact-byte promotion of an already accepted
  candidate.
- Matching SHA-256 sidecars and release notes that state the unsigned status and
  expected Windows warning behavior.

Trusted signing is not currently available to AirType. SignPath Foundation
declined the project's free-program application because the newly public
project does not yet have enough external adoption and visibility. Do not claim
that SignPath signs, certifies, endorses, or provides services for AirType.

## Validate Source

Run from the repository root:

```powershell
dotnet clean .\AirType\AirType.csproj
dotnet build .\AirType\AirType.csproj
.\tools\test.ps1 -NoRestore

Set-Location .\AirType.LocalAsrWorker
python -m pytest .\tests -q
Set-Location ..

.\tools\audit-dependencies.ps1
python .\tools\generate-notification-sound-candidates.py --verify-production
.\tools\build-public-snapshot.ps1
```

The public snapshot must not contain private history, developer paths,
credentials, databases, logs, recordings, internal plans, or generated output.

## Build An Unsigned Public Candidate

The explicit public-release switch enforces a clean `main` worktree while the
unsigned allowance keeps the signature state visible:

```powershell
.\tools\build-release.ps1 `
  -Version 1.0.0 `
  -PublicRelease `
  -AllowUnsignedPublicRelease
```

Both application output names contain `-unsigned`. The script publishes the
self-contained app, collects third-party license evidence, writes source
provenance, emits SHA-256 sidecars, and validates both distributions.

Verify independently:

```powershell
.\tools\verify-release-package.ps1 `
  -PackagePath .\release-artifacts\app\AirType-1.0.0-win-x64-unsigned.zip `
  -AllowUnsigned

.\tools\verify-windows-installer.ps1 `
  -PackagePath .\release-artifacts\app\AirType-1.0.0-win-x64-unsigned.msi `
  -ExpectedVersion 1.0.0 `
  -ExpectedInstallerSignatureStatus NotSigned `
  -ExpectedPayloadSignatureStatus NotSigned `
  -ExpectedSourceBranch main
```

Build and verify Local ASR separately:

```powershell
.\tools\package-local-asr-bundle.ps1 -Version 1.0.0 -PrepareRuntime -Force
.\tools\verify-local-asr-bundle.ps1 `
  -ManifestPath .\release-artifacts\local-asr\airtype-local-asr-small-en-ct2-win-x64-v1.0.0.json
```

The manual `Prepare unsigned stable release` workflow performs these operations
on a GitHub-hosted Windows runner and uploads reviewable artifacts. It has
read-only repository permissions and cannot create a GitHub Release.

## Exact-Byte Promotion For 1.0.0

The version 1.0.0 packages built by workflow run `29166944421` from source
commit `f1537b3` already passed package verification and disposable Windows 10
and Windows 11 acceptance. Because those guests were deleted after testing,
version 1.0.0 must promote those exact bytes rather than a rebuild.

Before promotion:

1. Download the MSI, ZIP, sidecars, and evidence from the public RC release.
2. Match their hashes against
   [`release-acceptance/1.0.0`](release-acceptance/1.0.0/README.md).
3. Confirm the MSI and embedded executable are `NotSigned`.
4. Scan the MSI and ZIP with the host's current Microsoft Defender definitions
   without installing either package.
5. Point release tag `v1.0.0` to `f1537b3`, the source commit recorded by the
   accepted packages.

The current `main` branch contains later policy-only release documentation.
Those commits do not alter the promoted package bytes.

## Clean-Machine Acceptance

For future builds, test only the MSI, ZIP, and matching sidecars on clean
Windows 10 x64 and Windows 11 x64 guests without developer runtimes:

1. Verify checksums and expected `NotSigned` states.
2. Install, launch, uninstall while retaining user data, reinstall, relaunch,
   and uninstall again.
3. Extract and launch the portable ZIP against an isolated profile.
4. Restart and confirm Settings, History, Notes, and Dictionary persist.
5. Install, exercise, remove, and reinstall Local ASR.
6. Verify hotkey, tray, startup, recording, cancellation, transcript insertion,
   history actions, audio download, and Clear History.
7. Verify light/dark themes, minimum size, and multi-monitor window behavior.
8. Confirm application removal and `%LOCALAPPDATA%\AirType\` data boundaries.

Record OS build, source commit, package hashes, signature states, and result.
Any rebuild or byte change invalidates prior exact-package acceptance.

## Publish

Create a GitHub Release from the exact source commit recorded by the accepted
package. Upload the MSI, portable ZIP, sidecars, Windows acceptance evidence,
and links to the matching Local ASR release.

The first visible section of the release notes must state:

- the packages are unsigned and use `-unsigned` filenames;
- Windows may display `Unknown publisher` or a SmartScreen unrecognized-app
  warning;
- users must download only from `samonjoat/AirType` and verify SHA-256 first;
- users must not disable Windows security protections globally; and
- the exact accepted hashes and source commit.

After upload, download every asset from GitHub, recheck hashes, verify Local ASR
installation resolves through the public URL, and confirm the repository's
CodeQL, Dependabot, and secret-scanning alert counts remain zero.

## Future Signing

AirType may reapply for free community signing after measurable public adoption
or adopt another sustainable signing option. Before publishing a signed release,
update the policy, packaging workflow, filenames, verification instructions,
and exact-package acceptance requirements. Never remove `-unsigned` or claim a
publisher identity until both the executable and installer carry valid trusted
signatures.
