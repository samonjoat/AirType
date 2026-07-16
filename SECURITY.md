# Security Policy

## Supported versions

Security fixes are provided for the latest published stable AirType release.
Pre-release and development builds are not supported for production use. The
current stable Windows release is explicitly unsigned; its support status comes
from publication on the official AirType GitHub Releases page, not from an
Authenticode signature.

## Reporting a vulnerability

Use GitHub's private vulnerability-reporting or security-advisory channel on
the [AirType project](https://github.com/samonjoat/AirType/security). If that
channel is unavailable, use the private support channel on the page where the
release was obtained. Do not open a public issue for an unpatched vulnerability.

Include the AirType version, Windows version, impact, reproduction steps, and
the minimum diagnostic detail needed to investigate. Do not include real API
keys, recordings, transcripts, or unrelated personal data. Replace them with
test data.

AirType maintainers will acknowledge a usable private report, investigate it,
and coordinate disclosure after a fix is available. Response times are not
guaranteed.

## Release verification

AirType 1.0.0 MSI and portable ZIP packages are not Authenticode signed. Their
official filenames contain `-unsigned`, and every distribution includes a
SHA-256 sidecar. Verify the GitHub Release URL and digest before running a
downloaded package. A missing signature is expected for 1.0.0, but a checksum
mismatch, unofficial source, or Windows malware detection is not.

Do not disable Microsoft Defender, SmartScreen, Smart App Control, or managed
security policy globally to run AirType. The repository's
[release integrity and code signing policy](CODE_SIGNING_POLICY.md) defines the
current signature status, maintainer approval, and build-origin controls.
