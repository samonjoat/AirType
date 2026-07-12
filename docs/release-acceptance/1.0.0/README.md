# AirType 1.0.0 Clean Windows Acceptance

Status: passed for the private unsigned release candidate. This is not a public
release approval or a claim that the executable is signed.

## Candidate

| Artifact | SHA-256 |
| --- | --- |
| `AirType-1.0.0-win-x64-unsigned.zip` | `ae16ba175d0d0acb17bca02b84780bdb736db9eca23b6dc0116fe2347e0e7b1a` |
| `AirType-1.0.0-win-x64-unsigned.msi` | `f238955190d9eb2ac4c143f81c1eb98ff84ca6fc7d46e2d08e8c751650f548b4` |
| `airtype-local-asr-small-en-ct2-win-x64-v1.0.0.zip` | `a508e64f52475628913515d339f1452f68ae3f43f47b6bb5424965bc0c093241` |

All artifacts were produced by private release-candidate workflow run
`29166944421` from source commit
`f1537b335b57a003a9050f69b3e4d8b6dbe836e9`. The portable application, MSI,
and Local ASR bundle passed their independent package verifiers before VM
testing.

## Environments

Disposable VirtualBox 7.2.2 VMs were installed from official Microsoft media.
No developer AirType profile, provider credential, or host Python/.NET
installation was copied into either VM.

| Environment | ISO SHA-256 | Result |
| --- | --- | --- |
| Windows 10 Enterprise LTSC Evaluation, build 19044 | `e4ab2e3535be5748252a8d5d57539a6e59be8d6726345ee10e7afd2cb89fefb5` | Passed |
| Windows 11 Pro, build 26200 | `baaeb6c90dd51648154b64c40c9e0c14d93a427f611a1bb49c8077fa2ff73364` | Passed |

Windows 10 had no installed .NET Desktop Runtime, Python runtime, or Windows App
Runtime package. The stock Windows 11 image contained four Windows App Runtime
packages. AirType still used its packaged self-contained runtime. Windows 10's
zero-package result independently proves that no Windows App Runtime
installation is required.

## Assertions

The same candidate files were copied into each VM and hash-checked before use.
The verifier then confirmed:

- AirType is version `1.0.0.0` and accurately identified as unsigned;
- the MSI installs under Program Files, creates its Start menu shortcut, launches,
  uninstalls without deleting user data, reinstalls, relaunches, and removes all
  installed files and shortcuts on final uninstall;
- first launch and restart both produce visible top-level window handles and
  isolated profile data;
- no .NET Desktop Runtime or Python installation exists on the guest;
- portable Python has no host-bound `pyvenv.cfg` or base installation;
- the pinned Visual C++ runtime is present app-locally;
- faster-whisper, CTranslate2, ONNX Runtime, and the PCM-only compatibility shim
  import from the extracted bundle;
- the bundled `small.en` model completes real CPU inference over 0.5 seconds of
  generated silence; and
- Local ASR removal leaves no engine installation directory.

Raw results: [Windows 10](windows-10.json) and
[Windows 11](windows-11.json).

Manual visual QA was completed before the installer integration. The integration
commit changed packaging, tests, workflows, and release documentation only; it
did not change application or Local ASR source. The exact-candidate clean-VM
verifier additionally observed nonzero top-level handles for installed,
reinstalled, first portable, and restarted portable launches in both operating
systems.

## Scope And Remaining Gates

The clean VMs validate MSI lifecycle and data retention, portable packaging,
first launch/restart, Local ASR inference, and removal. Cloud-provider calls were
intentionally excluded because no private credentials were copied into the VMs;
those workflows and the full application UI were tested separately before this
candidate was built.

The repository and candidate remain private. Before public visibility or a
stable GitHub Release, the replacement repository must pass the final private
audit, public-only GitHub security checks must pass after visibility cutover,
the final release artifact must satisfy the signing policy, and the maintainer
must give explicit publication approval.
