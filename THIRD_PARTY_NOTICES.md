# AirType Third-Party Notices

AirType includes and interacts with third-party software, models, and services.
Those items are governed by their own licenses and terms, not AirType's
`AGPL-3.0-only` license.

## Distributed components

The Windows package includes .NET libraries, the .NET Desktop Runtime, Windows
App SDK runtime files, SQLite, and a portable CPython environment with Local
ASR dependencies. The release build generates `THIRD_PARTY_LICENSES/` from the
exact NuGet and Python dependency artifacts in that package. Its `manifest.json`
is the authoritative version-level inventory, and each dependency folder
contains its supplied license/notice files or a metadata declaration and
license link when the artifact supplies no standalone license file.

Major components include:

- .NET and Microsoft Windows App SDK components under Microsoft license terms;
- MaterialDesignInXamlToolkit, CommunityToolkit.Mvvm, NAudio, Polly,
  Newtonsoft.Json, and other NuGet libraries under their respective licenses;
- Inter variable fonts under the SIL Open Font License 1.1, with the canonical
  notice included at `Fonts/Inter/OFL.txt`;
- SQLite and System.Data.SQLite under their applicable public-domain and
  license notices;
- CPython 3.12 and Python packages used by faster-whisper;
- Microsoft Visual C++ Redistributable runtime files used app-locally by Local
  ASR under Microsoft Software License Terms, with the extracted license and
  pinned source metadata included in the Local ASR inventory; and
- faster-whisper, CTranslate2, ONNX Runtime, NumPy, tokenizers, and their
  transitive dependencies.

AirType supplies normalized PCM directly to faster-whisper and does not ship or
use its optional PyAV/FFmpeg file-decoding dependency.

## Model downloaded on demand

Local ASR downloads `Systran/faster-whisper-small.en` from Hugging Face. The
model card identifies it as a CTranslate2 conversion of OpenAI's Whisper
`small.en` model and declares the MIT license:

- https://huggingface.co/Systran/faster-whisper-small.en
- https://github.com/openai/whisper/blob/main/LICENSE

The base AirType application package does not contain the model weights. A
separately distributed Local ASR bundle may contain them and must retain these
notices.

## Network services

Google Gemini, Groq, OpenRouter, Hugging Face, and any model provider reached
through OpenRouter are external services. Use of those services is governed by
their current terms, privacy policies, account controls, and pricing. Links are
provided in `PRIVACY.md`.

No third-party author or service provider endorses AirType merely because its
software or service is identified here.
