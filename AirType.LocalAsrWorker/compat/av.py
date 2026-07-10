"""Fail-closed compatibility shim for faster-whisper's optional audio decoder.

AirType streams normalized PCM16 and passes NumPy arrays to faster-whisper, so
its PyAV-based file decoder is not part of the supported worker path. The
locked faster-whisper release imports ``av`` unconditionally even for NumPy
input; this marker module satisfies that import without shipping FFmpeg.
"""

AIRTYPE_PCM_ONLY_SHIM = True


def __getattr__(name: str):
    raise RuntimeError(
        "PyAV decoding is unavailable in AirType's PCM-only Local ASR runtime "
        f"(attempted av.{name})."
    )
