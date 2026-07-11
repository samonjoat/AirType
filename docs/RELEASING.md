# AirType Release Procedure

This maintainer procedure produces the Windows x64 MSI installer, matching
portable ZIP, and Local ASR release assets. An unsigned validation candidate is
never a stable release.

## Release Requirements

- A clean commit on the public repository's `main` branch.
- The .NET SDK pinned by `global.json` on Windows x64.
- PowerShell, Python 3.12, and network access for dependency preparation/audits.
- Passing GitHub Actions build, test, dependency, secret, and snapshot checks.
- Passing CodeQL C# analysis after the repository becomes public.
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
.\tools\test.ps1 -NoRestore

Set-Location .\AirType.LocalAsrWorker
python -m pytest .\tests -q
Set-Location ..

.\tools\audit-dependencies.ps1
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

Both MSI and ZIP output names contain `-unsigned`. The script publishes self-contained,
collects exact third-party license evidence, writes release provenance and
checksums, and validates both distributions in isolated temporary directories.

Verify it independently:

```powershell
.\tools\verify-release-package.ps1 `
  -PackagePath .\release-artifacts\app\AirType-1.0.0-win-x64-unsigned.zip `
  -AllowUnsigned

.\tools\verify-windows-installer.ps1 `
  -PackagePath .\release-artifacts\app\AirType-1.0.0-win-x64-unsigned.msi `
  -ExpectedVersion 1.0.0 `
  -ExpectedInstallerSignatureStatus NotSigned `
  -ExpectedPayloadSignatureStatus NotSigned
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
  -InstallerPackagePath <app-msi> `
  -ExpectedInstallerSha256 <installer-sha256> `
  -LocalAsrPackagePath <local-asr-zip> `
  -ExpectedLocalAsrSha256 <local-asr-sha256> `
  -ExpectedVersion 1.0.0 `
  -OutputPath <evidence-json>
```

The `1.0.0` private candidate results and environment details are recorded in
[`release-acceptance/1.0.0`](release-acceptance/1.0.0/README.md).

## Clean-Machine Acceptance

Test only the MSI, ZIP, and matching checksum sidecars, never a developer build folder. On
clean Windows 10 x64 and Windows 11 x64 systems without .NET, Windows App SDK,
or Python installed:

1. Verify both checksums and expected signature states.
2. Install the MSI, launch it, uninstall it without deleting user data, reinstall,
   relaunch, and uninstall again.
3. Extract and launch the portable ZIP against the same isolated Windows profile.
4. Restart and confirm Settings, History, Notes, and Dictionary persist.
5. Install, use, remove, and reinstall Local ASR.
6. Run cloud ASR and cleanup with dedicated test credentials.
7. Verify hotkey, tray, startup, recording, cancellation, transcript insertion,
   history rerun/source toggle/edit/delete, audio download, and Clear History.
8. Verify light/dark themes, minimum size, and primary/secondary monitor window behavior.
9. Confirm storage remains under `%LOCALAPPDATA%\AirType\` and application files
   and Start menu shortcuts are removed by uninstall.

Record the OS build, source commit, artifact hash, signature state, and result.

## Public Security Cutover

The committed CodeQL workflow is intentionally skipped while this personal
repository is private because GitHub Code Security is not available without a
paid private-repository license. It activates automatically when the repository
becomes public.

After changing visibility, but before publishing any release:

1. Run the `CodeQL` workflow from `main` and confirm the `CodeQL (C#)` job
   completes successfully.
2. Resolve or explicitly triage every open code-scanning alert.
3. Add `CodeQL (C#)` to the required `main` branch checks.
4. Verify GitHub native secret scanning and private vulnerability reporting are
   enabled for the public repository; keep the independent Gitleaks workflow.
5. Re-run the final repository audit and confirm no required check is pending,
   skipped, or failing.

Do not enable CodeQL default setup in addition to this advanced workflow. The
manual build captures generated WPF/C# source and is the authoritative AirType
CodeQL configuration.

## SignPath Foundation Enrollment And Workflow

The repository can prepare for SignPath while private, but SignPath Foundation
requires an eligible project to be public, documented, and already released in
the form that will be signed. Enrollment therefore occurs only after the final
private audit and explicit publication approval:

1. Make the validated AirType repository public.
2. Publish the exact accepted unsigned MSI and portable ZIP as a clearly labeled GitHub
   **pre-release**, not a stable release.
3. Apply to SignPath Foundation and link the public AirType repository.
4. Install the SignPath GitHub App for the repository and configure an AirType
   project with one artifact configuration that signs only `AirType.exe` in the
   staging tree and a second configuration that signs the MSI itself. Both
   configurations enforce product name `AirType` and accept a required `version`
   parameter for product/file version restrictions.
5. Configure a signing policy with manual approval and assign the roles listed
   in [`../CODE_SIGNING_POLICY.md`](../CODE_SIGNING_POLICY.md).
6. Configure the GitHub `release-signing` environment and these repository
   values:

   - secret `SIGNPATH_API_TOKEN`;
   - variable `SIGNPATH_ORGANIZATION_ID`;
   - variable `SIGNPATH_PROJECT_SLUG`;
   - variable `SIGNPATH_SIGNING_POLICY_SLUG`;
   - variable `SIGNPATH_PAYLOAD_ARTIFACT_CONFIGURATION_SLUG`;
   - variable `SIGNPATH_INSTALLER_ARTIFACT_CONFIGURATION_SLUG`.

The manual `Sign release package` workflow runs only from `main`. It builds and
validates the public snapshot, preserves the unsigned staging tree, uploads that
tree to GitHub, submits its GitHub artifact ID to SignPath, waits for approval,
and rejects the returned tree unless `AirType.exe` has a valid Authenticode
signature from SignPath Foundation with matching clean-source provenance. It
then builds the MSI from that signed payload, submits the MSI through the second
artifact configuration, and rejects it unless both the MSI and its embedded
executable validate. The workflow uploads the verified ZIP, MSI, and sidecars;
it never creates a GitHub Release.

After signing, download the returned ZIP, MSI, and checksums, record their exact hashes,
and repeat Windows 10 and Windows 11 clean-machine acceptance. A byte change,
rebuild, different signature, or different hash invalidates earlier acceptance.

## Produce A Signed Stable Package

For local certificate signing, set the protected certificate thumbprint and run:

```powershell
$env:AIRTYPE_SIGNING_CERTIFICATE_THUMBPRINT = '<protected thumbprint>'
.\tools\build-release.ps1 -Version 1.0.0 -PublicRelease
```

After SignPath enrollment, use the manual `Sign release package` GitHub Actions
workflow. It signs the executable staging tree, runs
`finalize-signpath-release.ps1`, signs the resulting installer, and runs
`finalize-signpath-installer.ps1`. In either the local or SignPath path, the
signed executable and MSI must correspond to the tested source and release
manifest. Any rebuild or byte change requires package verification and
clean-machine acceptance again.

## Publish

Create a GitHub Release from the exact tested `main` commit. Upload the signed
MSI, portable ZIP, both checksums, Local ASR manifest/bundle/checksums, release notes, license,
privacy notice, installation guide, and generated third-party inventory.

After upload, verify all assets from GitHub, test Local ASR installation through
the public URL, and only then mark the release stable. The Local ASR tag must be
`local-asr-small-en-ct2-v<version>` and its three asset filenames must exactly
match the generated ZIP, JSON, and SHA-256 sidecar. Keep the repository private
until source, workflows, and private fresh-clone validation pass; change
visibility only with explicit maintainer approval.
