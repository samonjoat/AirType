"""Transcriber implementations for AirType's local ASR worker."""

from __future__ import annotations

from dataclasses import dataclass
import os
from typing import Iterable, Protocol

from .audio import SAMPLE_RATE_HZ


DEFAULT_MODEL_SIZE = "base.en"
DEFAULT_COMPUTE_TYPE = "int8"
DEFAULT_DEVICE = "cpu"


@dataclass(frozen=True)
class TranscriptionResult:
    text: str
    raw_text: str
    language: str | None = None
    duration_s: float | None = None


class Transcriber(Protocol):
    def load_model(self) -> None:
        ...

    def prewarm(self) -> None:
        ...

    def transcribe_pcm16(
        self,
        pcm: bytes,
        *,
        initial_prompt: str | None = None,
        hotwords: str | None = None,
    ) -> TranscriptionResult:
        ...


class MockTranscriber:
    """Deterministic transcriber for tests and protocol development."""

    def __init__(self, responses: Iterable[str] | None = None) -> None:
        self.loaded = False
        self.prewarmed = False
        self.prompts: list[str | None] = []
        self._responses = list(responses or [])
        self._count = 0

    def load_model(self) -> None:
        self.loaded = True

    def prewarm(self) -> None:
        self.prewarmed = True

    def transcribe_pcm16(
        self,
        pcm: bytes,
        *,
        initial_prompt: str | None = None,
        hotwords: str | None = None,
    ) -> TranscriptionResult:
        self.prompts.append(initial_prompt)
        if self._responses:
            text = self._responses.pop(0)
        else:
            self._count += 1
            text = f"mock utterance {self._count}"
        return TranscriptionResult(text=text, raw_text=text, language="en")


class FasterWhisperTranscriber:
    """Lazy wrapper around faster_whisper.WhisperModel."""

    def __init__(
        self,
        *,
        model_size: str = DEFAULT_MODEL_SIZE,
        model_dir: str | None = None,
        cpu_threads: int = 8,
        compute_type: str = DEFAULT_COMPUTE_TYPE,
        device: str = DEFAULT_DEVICE,
    ) -> None:
        self.model_size = model_size
        self.model_dir = model_dir
        self.cpu_threads = cpu_threads
        self.compute_type = compute_type
        self.device = device
        self._model = None

    @property
    def loaded(self) -> bool:
        return self._model is not None

    def load_model(self) -> None:
        if self._model is not None:
            return

        try:
            from faster_whisper import WhisperModel  # type: ignore
        except Exception as exc:
            raise RuntimeError("faster-whisper is required for real transcription") from exc

        model_source = self.model_size
        if self.model_dir and os.path.isfile(os.path.join(self.model_dir, "config.json")):
            model_source = self.model_dir

        self._model = WhisperModel(
            model_source,
            device=self.device,
            compute_type=self.compute_type,
            cpu_threads=self.cpu_threads,
            download_root=None if model_source == self.model_dir else self.model_dir,
        )

    def prewarm(self) -> None:
        silence = b"\x00\x00" * (SAMPLE_RATE_HZ // 2)
        self.transcribe_pcm16(silence, initial_prompt=None)

    def transcribe_pcm16(
        self,
        pcm: bytes,
        *,
        initial_prompt: str | None = None,
        hotwords: str | None = None,
    ) -> TranscriptionResult:
        self.load_model()
        assert self._model is not None

        try:
            import numpy as np  # type: ignore
        except Exception as exc:
            raise RuntimeError("numpy is required for real transcription") from exc

        audio = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0
        segments, info = self._model.transcribe(
            audio,
            language="en",
            beam_size=5,
            condition_on_previous_text=False,
            vad_filter=False,
            no_speech_threshold=0.6,
            log_prob_threshold=-1.0,
            initial_prompt=initial_prompt,
            hotwords=hotwords or None,
            word_timestamps=False,
        )
        parts = [segment.text.strip() for segment in segments if segment.text and segment.text.strip()]
        text = " ".join(parts).strip()
        duration = float(getattr(info, "duration", 0.0) or 0.0)
        language = getattr(info, "language", "en")
        return TranscriptionResult(text=text, raw_text=text, language=language, duration_s=duration)


def create_transcriber(
    *,
    model_dir: str | None,
    cpu_threads: int,
    mock: bool = False,
    model_size: str = DEFAULT_MODEL_SIZE,
) -> Transcriber:
    if mock or os.environ.get("AIRTYPE_ASR_MOCK_TRANSCRIBER") == "1":
        return MockTranscriber()
    return FasterWhisperTranscriber(model_size=model_size, model_dir=model_dir, cpu_threads=cpu_threads)
