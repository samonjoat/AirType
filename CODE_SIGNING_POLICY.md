# AirType Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate
by [SignPath Foundation](https://signpath.org/).

## Scope

Official stable Windows packages contain an Authenticode-signed `AirType.exe`,
and the MSI installer is independently Authenticode signed.
The AirType signing policy does not sign bundled third-party executables or
libraries as though they were produced by AirType. Those components retain
their upstream identities, licenses, and signature states.

Unsigned artifacts whose names contain `-unsigned` are validation candidates.
They are not official stable releases.

## Team Roles

- Authors and committers: [`@samonjoat`](https://github.com/samonjoat)
- Reviewers: [`@samonjoat`](https://github.com/samonjoat)
- Signing approvers: [`@samonjoat`](https://github.com/samonjoat)

AirType is currently maintained by one person. Contributions from other people
must be reviewed by the maintainer before merge. Every signing request requires
manual approval through the configured SignPath signing policy.
Every person assigned one of these roles must keep multi-factor authentication
enabled for both GitHub and SignPath access.

## Build And Signing Controls

- Signed artifacts must be built from the public AirType repository by the
  committed GitHub Actions workflow on a GitHub-hosted Windows runner.
- Signing is permitted only for a clean commit on the protected `main` branch.
- Build, test, dependency, secret, provenance, and public-snapshot checks must
  pass before a signing request is submitted.
- The SignPath integration receives the GitHub artifact identifier directly
  from GitHub Actions and returns the signed artifact to the same workflow.
- The finalizers require valid Authenticode signatures whose signer subjects
  contain `SignPath Foundation`, matching release provenance, and passing strict
  ZIP and MSI verification before producing stable artifacts.
- The signing workflow uploads artifacts for maintainer review but does not
  create or publish a GitHub Release.

## Privacy And Network Behavior

AirType does not send telemetry to an AirType-operated service. It sends audio
or transcript content only when the user configures and invokes a cloud
provider or permits a configured cloud fallback. Local ASR model/runtime setup
downloads explicitly requested release assets. See the complete
[AirType Privacy Notice](PRIVACY.md), including provider-specific disclosures
and user controls.

## Verification

Stable releases include SHA-256 sidecars. Users should verify those digests and
the MSI and extracted `AirType.exe` Authenticode signatures as described in
[`docs/INSTALL.md`](docs/INSTALL.md). A signed package is not stable until its
exact hash has passed the release-acceptance process and is published from the
official AirType GitHub repository.
