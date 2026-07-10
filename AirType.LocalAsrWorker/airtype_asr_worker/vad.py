"""Voice activity segmentation wrappers.

The production worker keeps VAD imports lazy so fast protocol tests can run
without model assets or ONNX runtime initialization.
"""

from __future__ import annotations

from dataclasses import dataclass
import os

from .protocol import PcmFrame


SAMPLE_RATE_HZ = 16000


@dataclass(frozen=True)
class VadConfig:
    threshold: float = 0.5
    min_speech_duration_ms: int = 250
    max_speech_duration_s: float = 15.0
    min_silence_duration_ms: int = 500
    speech_pad_ms: int = 200
    mock_mode: bool = True


@dataclass(frozen=True)
class SpeechSegment:
    pcm: bytes
    first_sample: int
    final: bool = True


class MockVadSegmenter:
    """Buffers all frames and returns one segment when flushed."""

    def __init__(self, config: VadConfig | None = None) -> None:
        self.config = config or VadConfig(mock_mode=True)
        self._chunks: list[bytes] = []
        self._first_sample: int | None = None

    def accept_frame(self, frame: PcmFrame) -> list[SpeechSegment]:
        if self._first_sample is None:
            self._first_sample = frame.first_sample
        self._chunks.append(frame.payload)
        return []

    def flush(self) -> list[SpeechSegment]:
        if not self._chunks:
            return []
        segment = SpeechSegment(
            pcm=b"".join(self._chunks),
            first_sample=self._first_sample or 0,
            final=True,
        )
        self.reset()
        return [segment]

    def reset(self) -> None:
        self._chunks.clear()
        self._first_sample = None


class OnnxVadSegmenter(MockVadSegmenter):
    """Placeholder for ONNX-backed VAD with the same streaming interface.

    The import is deliberately deferred. Until a model path is wired in by the
    host app, this class behaves like the mock segmenter after verifying that
    onnxruntime can be imported.
    """

    def __init__(self, config: VadConfig | None = None) -> None:
        try:
            import onnxruntime  # noqa: F401
        except Exception as exc:
            raise RuntimeError("onnxruntime is required for ONNX VAD mode") from exc
        super().__init__(config or VadConfig(mock_mode=False))


class SileroVadSegmenter(MockVadSegmenter):
    """Silero VAD segmenter using faster-whisper's bundled VAD helper."""

    def __init__(self, config: VadConfig | None = None) -> None:
        super().__init__(config or VadConfig(mock_mode=False))
        self._emitted_until_sample = 0
        self._frames_since_vad = 0

    def accept_frame(self, frame: PcmFrame) -> list[SpeechSegment]:
        if self._first_sample is None:
            self._first_sample = frame.first_sample
        self._chunks.append(frame.payload)
        self._frames_since_vad += 1
        if self._frames_since_vad < 5:
            return []
        self._frames_since_vad = 0
        return self._finalized_segments(allow_tail=False)

    def flush(self) -> list[SpeechSegment]:
        segments = self._finalized_segments(allow_tail=True)
        self.reset()
        return segments

    def reset(self) -> None:
        super().reset()
        self._emitted_until_sample = 0
        self._frames_since_vad = 0

    def _finalized_segments(self, *, allow_tail: bool) -> list[SpeechSegment]:
        pcm = b"".join(self._chunks)
        if not pcm:
            return []

        try:
            import numpy as np  # type: ignore
            from faster_whisper.vad import VadOptions, get_speech_timestamps  # type: ignore
        except Exception as exc:
            raise RuntimeError("faster-whisper and numpy are required for Silero VAD") from exc

        audio = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0
        options = VadOptions(
            threshold=self.config.threshold,
            min_speech_duration_ms=self.config.min_speech_duration_ms,
            max_speech_duration_s=self.config.max_speech_duration_s,
            min_silence_duration_ms=self.config.min_silence_duration_ms,
            speech_pad_ms=self.config.speech_pad_ms,
        )
        timestamps = get_speech_timestamps(audio, options, sampling_rate=SAMPLE_RATE_HZ)
        if not timestamps and allow_tail and self._emitted_until_sample == 0:
            return [SpeechSegment(pcm=pcm, first_sample=self._first_sample or 0, final=True)]

        total_samples = len(pcm) // 2
        silence_margin_samples = int((self.config.min_silence_duration_ms + self.config.speech_pad_ms) * SAMPLE_RATE_HZ / 1000)
        output: list[SpeechSegment] = []
        for item in timestamps:
            start = int(item["start"])
            end = int(item["end"])
            if end <= self._emitted_until_sample:
                continue
            if not allow_tail and end > total_samples - silence_margin_samples:
                continue

            start_byte = max(0, start * 2)
            end_byte = min(len(pcm), end * 2)
            if end_byte <= start_byte:
                continue

            output.append(SpeechSegment(
                pcm=pcm[start_byte:end_byte],
                first_sample=(self._first_sample or 0) + start,
                final=True,
            ))
            self._emitted_until_sample = end

        if output:
            self._trim_emitted_audio(pcm)

        return output

    def _trim_emitted_audio(self, pcm: bytes) -> None:
        speech_pad_samples = int(self.config.speech_pad_ms * SAMPLE_RATE_HZ / 1000)
        trim_samples = max(0, self._emitted_until_sample - speech_pad_samples)
        if trim_samples <= 0:
            return

        trim_bytes = min(len(pcm), trim_samples * 2)
        self._chunks = [pcm[trim_bytes:]] if trim_bytes < len(pcm) else []
        self._first_sample = (self._first_sample or 0) + trim_samples
        self._emitted_until_sample -= trim_samples


def create_vad_segmenter(config: VadConfig | None = None, *, mock: bool = False) -> MockVadSegmenter:
    if mock or os.environ.get("AIRTYPE_ASR_MOCK_VAD") == "1":
        return MockVadSegmenter(config or VadConfig(mock_mode=True))
    return SileroVadSegmenter(config or VadConfig(mock_mode=False))
