# Security Policy

## Supported versions

Security fixes are provided for the latest published stable AirType release.
Pre-release, development, and unsigned builds are not supported for production
use.

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

Official public packages are Authenticode signed and include a `.sha256`
sidecar. Do not treat an artifact whose name contains `-unsigned` as an official
public release. Verify the SHA-256 digest before running a downloaded package.
