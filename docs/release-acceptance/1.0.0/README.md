# AirType 1.0.0 Clean Windows Acceptance

Status: passed for the private unsigned release candidate. This is not a public
release approval or a claim that the executable is signed.

## Candidate

| Artifact | SHA-256 |
| --- | --- |
| `AirType-1.0.0-win-x64-unsigned.zip` | `33cd18122376ddcd92294254ba599e797c0b9702e77a700a2a5844b4f9581c29` |
| `airtype-local-asr-small-en-ct2-win-x64-v1.0.0.zip` | `a6f05138713b2eae78cb573fe51c201731ad34ab06ed7d5b8cfc58491e646ac8` |

The Local ASR artifact records source commit
`150eef37825e0405194d9c6c49c475e53e20689c` and passed the independent bundle
verifier before VM testing.

## Environments

Disposable VirtualBox 7.2.2 VMs were installed from official Microsoft 90-day
Enterprise evaluation media. No developer AirType profile, provider credential,
or host Python/.NET installation was copied into either VM.

| Environment | ISO SHA-256 | Result |
| --- | --- | --- |
| Windows 10 Enterprise LTSC Evaluation, build 19044 | `e4ab2e3535be5748252a8d5d57539a6e59be8d6726345ee10e7afd2cb89fefb5` | Passed |
| Windows 11 Enterprise Evaluation, build 26200 | `a61adeab895ef5a4db436e0a7011c92a2ff17bb0357f58b13bbc4062e535e7b9` | Passed |

Windows 10 had no installed .NET Desktop Runtime, Python runtime, or Windows App
Runtime package. The stock Windows 11 image contained three Windows App Runtime
packages. Two removable packages were removed; Windows retained version 1.3
because Screen Sketch, Alarms, and Teams depend on it. AirType still used its
packaged self-contained runtime. Windows 10's zero-package result independently
proves that no Windows App Runtime installation is required.

## Assertions

The same candidate files were copied into each VM and hash-checked before use.
The verifier then confirmed:

- AirType is version `1.0.0.0` and accurately identified as unsigned;
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

Manual visual QA was completed against the same application artifact hash. The
clean-VM verifier additionally observed nonzero top-level handles on both app
launches in both operating systems.

## Scope And Remaining Gates

The clean VMs validate packaging portability, first launch/restart, Local ASR,
and removal. Cloud-provider calls were intentionally excluded because no private
credentials were copied into the VMs; those workflows and the full application
UI were tested separately before this candidate was built.

The repository and candidate remain private. Before public visibility or a
stable GitHub Release, the sanitized replacement repository must pass a private
fresh-clone/workflow audit, the final release artifact must satisfy the signing
policy, and the maintainer must give explicit publication approval.
