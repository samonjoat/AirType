"""Runtime metrics for the ASR worker."""

from __future__ import annotations

from dataclasses import dataclass
import os
import time
from typing import Any


@dataclass
class WorkerMetrics:
    frames_received: int = 0
    bytes_received: int = 0
    utterances_final: int = 0
    sequence_warnings: int = 0
    hotword_token_count: int = 0
    model_loaded: bool = False
    asr_total_ms: int = 0

    def note_frame(self, byte_count: int) -> None:
        self.frames_received += 1
        self.bytes_received += byte_count

    def note_utterance(self) -> None:
        self.utterances_final += 1

    def note_asr(self, asr_ms: int) -> None:
        self.asr_total_ms += max(0, asr_ms)

    def note_sequence_warning(self) -> None:
        self.sequence_warnings += 1

    def snapshot(self, *, active_recording: bool) -> dict[str, Any]:
        snapshot: dict[str, Any] = {
            "activeRecording": active_recording,
            "framesReceived": self.frames_received,
            "bytesReceived": self.bytes_received,
            "utterancesFinal": self.utterances_final,
            "asrTotalMs": self.asr_total_ms,
            "sequenceWarnings": self.sequence_warnings,
            "hotwordTokenCount": self.hotword_token_count,
            "modelLoaded": self.model_loaded,
            "processId": os.getpid(),
            "monotonicMs": int(time.monotonic() * 1000),
        }

        process_memory = _process_memory_bytes()
        if process_memory is not None:
            snapshot["processRssBytes"] = process_memory
        return snapshot


def _process_memory_bytes() -> int | None:
    try:
        import psutil  # type: ignore
    except Exception:
        return None

    try:
        return int(psutil.Process(os.getpid()).memory_info().rss)
    except Exception:
        return None
