# AirType Release Integrity And Code Signing Policy

## Current Signing Status

AirType 1.0.0 Windows packages are not Authenticode signed. Their filenames
contain `-unsigned`, and Windows may identify the publisher as unknown or show
a Microsoft Defender SmartScreen reputation warning.

SignPath Foundation did not approve AirType for its free certificate program
because the newly public project does not yet have enough external adoption and
visibility. AirType does not claim that SignPath.io signs, certifies, endorses,
or provides release services for the project.

An unsigned warning is not evidence that Microsoft detected malware, but it is
also not proof that a file is safe. Users must verify the download source and
SHA-256 digest before running a package.

## Official Release Scope

Official packages are published only on the
[AirType GitHub Releases page](https://github.com/samonjoat/AirType/releases).
For an unsigned stable release, all of the following must be true:

- the GitHub Release is marked stable rather than draft or pre-release;
- the MSI and ZIP filenames contain `-unsigned`;
- each package has a matching SHA-256 sidecar;
- the release notes identify the source commit and exact accepted hashes; and
- the exact package bytes passed the documented release-acceptance process.

Files from third-party download sites, mirrors, forks, issue attachments, or
unofficial rebuilds are not official AirType releases even if their names match.

## Maintainer And Approval

- Authors, reviewers, and release approvers:
  [`@samonjoat`](https://github.com/samonjoat)

AirType is currently maintained by one person. Contributions from other people
must be reviewed before merge. GitHub multi-factor authentication remains
enabled for maintainer access. Publishing a stable release is a separate manual
maintainer action after build, test, security, provenance, and acceptance gates
pass.

## Build And Provenance Controls

- Release source must be a clean commit from the protected public repository.
- Required CI, dependency, secret, CodeQL, and public-snapshot checks must pass.
- Build output records its source commit, source branch, version, signature
  state, and executable SHA-256 digest in `release-manifest.json`.
- Unsigned public builds require the explicit
  `-AllowUnsignedPublicRelease` packaging switch and retain `-unsigned` in
  their filenames.
- The MSI, portable ZIP, and Local ASR bundle are independently verified before
  acceptance testing.
- A rebuild or any byte change produces a new hash and invalidates prior
  exact-package acceptance.

AirType 1.0.0 promotes the exact packages built from source commit `f1537b3`
and accepted on clean Windows 10 and Windows 11 guests. Later policy-only
commits do not alter those already-tested package bytes.

## User Verification

Follow [`docs/INSTALL.md`](docs/INSTALL.md) before running AirType. In summary:

1. Download only from the official GitHub Release.
2. Calculate SHA-256 locally and compare it with the attached sidecar and the
   digest printed in the release notes.
3. Expect `NotSigned` or `Unknown publisher` for AirType 1.0.0.
4. Do not continue if the hash differs, the file came from another location,
   or Windows reports malware or a potentially unwanted application.
5. Do not disable Microsoft Defender, SmartScreen, Smart App Control, or an
   organization's security policy globally to install AirType.

## Future Signing

AirType may reapply for community code signing after it develops sufficient
public adoption or may adopt another sustainable signing option. This policy,
the release workflow, filenames, verification instructions, and acceptance
requirements must be updated before any package is represented as signed.

## Privacy And Network Behavior

AirType does not send telemetry to an AirType-operated service. Audio or text is
sent only when the user configures and invokes a cloud provider or permits a
configured cloud fallback. Local ASR setup downloads explicitly requested
release assets. See the complete [AirType Privacy Notice](PRIVACY.md).
