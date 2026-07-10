"""Audio frame tracking utilities."""

from __future__ import annotations

from dataclasses import dataclass, field

from .protocol import PcmFrame, ProtocolError


SAMPLE_RATE_HZ = 16_000
CHANNELS = 1
BYTES_PER_SAMPLE = 2


@dataclass(frozen=True)
class SequenceGap:
    expected: int
    actual: int


@dataclass
class AudioAccumulator:
    """Accumulates PCM16 mono frames for a single active recording."""

    expected_sequence: int | None = None
    total_bytes: int = 0
    total_samples: int = 0
    sequence_gaps: list[SequenceGap] = field(default_factory=list)
    _chunks: list[bytes] = field(default_factory=list)

    def add_frame(self, frame: PcmFrame) -> SequenceGap | None:
        if len(frame.payload) % BYTES_PER_SAMPLE:
            raise ProtocolError("PCM payload must be 16-bit aligned")

        gap = None
        if self.expected_sequence is not None and frame.sequence != self.expected_sequence:
            gap = SequenceGap(expected=self.expected_sequence, actual=frame.sequence)
            self.sequence_gaps.append(gap)

        self.expected_sequence = frame.sequence + 1
        self.total_bytes += len(frame.payload)
        self.total_samples += frame.sample_count
        self._chunks.append(frame.payload)
        return gap

    def pcm_bytes(self) -> bytes:
        return b"".join(self._chunks)

    def clear(self) -> None:
        self.expected_sequence = None
        self.total_bytes = 0
        self.total_samples = 0
        self.sequence_gaps.clear()
        self._chunks.clear()

