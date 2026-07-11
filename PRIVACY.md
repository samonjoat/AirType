# AirType Privacy Notice

Last updated: July 10, 2026

This notice describes how AirType 1.0 handles data. AirType is a Windows
desktop application. It does not require an AirType account and, as shipped,
does not send product analytics or telemetry to an AirType-operated server.
Some transcription and cleanup features send data directly to services you
configure.

## Data stored on your computer

AirType stores application data under `%LOCALAPPDATA%\AirType\`, including:

- recorded audio as WAV files;
- transcription history in SQLite, including ASR and cleaned transcript text,
  timestamps, duration, provider/model metadata, and workflow status;
- transcription trace JSON containing transcript text and provider request and
  response details;
- application diagnostic logs;
- settings, notes, prompts, and dictionary vocabulary/corrections; and
- provider API keys encrypted with Windows Data Protection API for the current
  Windows user.

Anyone or any software with access to your Windows account or local files may
be able to access unencrypted recordings, transcripts, notes, prompts,
dictionary entries, and logs. Protect your Windows account and device.

## Data sent to other services

The data sent depends on your selected ASR, cleanup, and fallback settings.

### Local ASR

Local ASR processes recording audio on your computer. Installing Local ASR
downloads a checksummed portable runtime and `small.en` model bundle from the
official AirType GitHub Release. The included model originates from the
Systran/faster-whisper-small.en Hugging Face repository. Installing the bundle
does not send your recording or transcript. If a cloud provider credential is
saved and local ASR cannot complete the request, AirType may send the recording
to a cloud fallback provider. The current fallback priority is Groq, then OpenRouter,
then Gemini, based on which credentials are available.

### Cloud ASR

When Google Gemini, Groq, or OpenRouter is selected for ASR, AirType sends the
recording and request instructions to that provider. Depending on the selected
provider and model, request instructions may include the active transcription
prompt and dictionary entries. OpenRouter may route a request to the model
provider selected through its service.

### Transcript cleanup

When transcript cleanup is enabled, AirType sends the raw ASR transcript and
cleanup instructions to the selected cleanup provider. Depending on your
settings, the request can also include cleanup context, relevant dictionary
entries, and custom prompt guidance. This can occur even when ASR itself ran
locally.

AirType does not control a provider's storage, review, training, routing, or
deletion practices. Review the terms for the service and account tier you use:

- [Google Gemini API terms](https://ai.google.dev/gemini-api/terms) and
  [Zero Data Retention guidance](https://ai.google.dev/gemini-api/docs/zdr)
- [Groq privacy policy](https://groq.com/privacy-policy) and
  [customer-data documentation](https://console.groq.com/docs/your-data)
- [OpenRouter privacy policy](https://openrouter.ai/privacy),
  [data-collection controls](https://openrouter.ai/docs/guides/privacy/data-collection),
  and [Zero Data Retention controls](https://openrouter.ai/docs/guides/features/zdr)
- [Hugging Face privacy policy](https://huggingface.co/privacy)

Provider terms and controls can change independently of AirType.

## Retention and deletion

The default audio-retention setting is **Never Delete**. Selecting 7, 30, or
90 days deletes local WAV recordings older than that period and their matching
transcription trace JSON.
Timed retention does **not** delete transcript-history rows from SQLite.

Application diagnostic logs use a separate storage limit. AirType keeps the
current daily application log and up to seven historical application logs.
Older application logs are deleted on a best-effort basis.

Deleting a History session removes that session's database row, managed audio
file, and matching transcription trace. Clearing History performs that action
for all loaded history sessions. File deletion is best effort; locked files or
storage errors can prevent removal, so verify the local folder if deletion is
critical.

Notes, prompts, dictionary entries, settings, and credentials remain until you
delete them through the application where that option exists, or remove the
AirType data folder. Removing `%LOCALAPPDATA%\AirType\` while AirType is closed
deletes the app's local data for that Windows profile. Provider-side copies are
subject to the provider's own policy and must be managed with that provider.

## Your choices

You can reduce disclosure by using Local ASR without saved cloud credentials,
disabling transcript cleanup, omitting sensitive dictionary/custom-prompt
content, and deleting local sessions after use. You can remove saved provider
keys in Settings.

Do not use AirType to record another person where notice or consent is
required unless you have obtained it. Do not submit data you are prohibited
from sending to the selected provider.

## Changes and contact

Material changes will be published with a new "Last updated" date. For privacy
questions, use the support or private reporting channel on the official
[AirType project page](https://github.com/samonjoat/AirType). Do not include
recordings, transcripts, API keys, or other sensitive data in a public report.
