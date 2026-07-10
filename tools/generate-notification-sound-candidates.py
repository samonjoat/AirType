#!/usr/bin/env python3
"""Generate deterministic Pulse-family notification cues and verify production assets.

Requires Python 3.10+ and NumPy. The script never writes to AirType/Sounds.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import wave
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path

import numpy as np


SAMPLE_RATE = 44_100
CHANNELS = 1
BITS_PER_SAMPLE = 16
WAVEFORM_BINS = 96
EVENTS = ("start", "done", "cancel")
REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT_ROOT = REPO_ROOT / "design-assets" / "notification-sound-lab"
PRODUCTION_SOUNDS = REPO_ROOT / "AirType" / "Sounds"

PRODUCTION_SELECTION = {
    "start": (
        "04-pulse-low",
        "7d8b5a74ee54a7a5d2b93547ad6682de3bf2cd2a002208d7bea24525ec94eec0",
    ),
    "done": (
        "03-pulse-warm",
        "b55bf39e4ea7aa0a0eea6c359a6deca04fdea8d814f6ecf70c834aee92a8621e",
    ),
    "cancel": (
        "03-pulse-warm",
        "7bfa7b456e3213a76a2a8775f07c58565811b0faba93be20d7f678e3e8f68fae",
    ),
}


@dataclass(frozen=True)
class PairSpec:
    frequencies: tuple[float, float]
    durations_ms: tuple[int, int]
    gap_ms: int
    gains: tuple[float, float]
    target_peak: float


@dataclass(frozen=True)
class Variation:
    id: str
    name: str
    character: str
    tags: tuple[str, str]
    harmonics: tuple[float, ...]
    attack_ms: float
    decay_ms: float
    transient: float
    pitch_drop_cents: float
    lowpass_hz: float
    reflection: float
    drive: float
    start: PairSpec
    done: PairSpec
    cancel: PairSpec


def pair(
    frequencies: tuple[float, float],
    durations_ms: tuple[int, int],
    gap_ms: int,
    target_peak: float,
    gains: tuple[float, float] = (0.78, 1.0),
) -> PairSpec:
    return PairSpec(frequencies, durations_ms, gap_ms, gains, target_peak)


VARIATIONS = (
    Variation(
        "01-pulse-soft", "Pulse Soft", "Gentle rounded double pulse", ("soft", "balanced"),
        (1.0, 0.10, 0.025), 4.5, 43, 0.006, 7, 4_200, 0.035, 1.05,
        pair((523.25, 659.25), (44, 62), 11, 0.160),
        pair((293.66, 392.00), (48, 94), 12, 0.108),
        pair((440.00, 293.66), (50, 102), 12, 0.160),
    ),
    Variation(
        "02-pulse-tight", "Pulse Tight", "Fast dry pulse with firm edges", ("compact", "dry"),
        (1.0, 0.17, 0.045), 2.2, 31, 0.014, 11, 4_800, 0.012, 1.12,
        pair((554.37, 698.46), (36, 50), 8, 0.160),
        pair((329.63, 440.00), (38, 76), 9, 0.108),
        pair((466.16, 311.13), (42, 82), 9, 0.160),
    ),
    Variation(
        "03-pulse-warm", "Pulse Warm", "Lower pulse with a mellow body", ("warm", "low fatigue"),
        (1.0, 0.20, 0.055, 0.015), 5.5, 52, 0.009, 9, 3_500, 0.055, 1.08,
        pair((466.16, 587.33), (48, 68), 13, 0.158),
        pair((261.63, 349.23), (52, 106), 14, 0.106),
        pair((392.00, 261.63), (54, 112), 14, 0.158),
    ),
    Variation(
        "04-pulse-low", "Pulse Low", "Deep restrained pulse for quiet rooms", ("deep", "restrained"),
        (1.0, 0.14, 0.035), 5.0, 55, 0.007, 6, 3_200, 0.045, 1.06,
        pair((415.30, 523.25), (50, 70), 13, 0.158),
        pair((246.94, 329.63), (54, 112), 14, 0.106),
        pair((349.23, 233.08), (56, 118), 14, 0.158),
    ),
    Variation(
        "05-pulse-clean", "Pulse Clean", "Clear pulse without bell overtones", ("clear", "neutral"),
        (1.0, 0.065, 0.012), 3.5, 39, 0.004, 5, 4_600, 0.022, 1.04,
        pair((587.33, 739.99), (42, 58), 10, 0.160),
        pair((349.23, 466.16), (46, 88), 11, 0.108),
        pair((493.88, 329.63), (48, 96), 11, 0.160),
    ),
    Variation(
        "06-pulse-wood", "Pulse Wood", "Short tactile pulse with a wooden knock", ("tactile", "muted"),
        (1.0, 0.28, 0.09, 0.025), 1.8, 28, 0.042, 19, 3_600, 0.018, 1.18,
        pair((493.88, 622.25), (38, 52), 9, 0.160),
        pair((293.66, 392.00), (42, 82), 10, 0.108),
        pair((415.30, 277.18), (44, 88), 10, 0.160),
    ),
    Variation(
        "07-pulse-haptic", "Pulse Haptic", "Compact pulse with a soft physical tap", ("physical", "defined"),
        (1.0, 0.22, 0.06), 2.0, 33, 0.032, 16, 3_900, 0.010, 1.16,
        pair((523.25, 659.25), (38, 52), 8, 0.160),
        pair((311.13, 415.30), (40, 80), 9, 0.108),
        pair((440.00, 293.66), (42, 88), 9, 0.160),
    ),
    Variation(
        "08-pulse-air", "Pulse Air", "Soft open pulse with a light breath", ("open", "gentle"),
        (1.0, 0.09, 0.025), 6.5, 48, 0.018, 6, 4_100, 0.075, 1.03,
        pair((554.37, 698.46), (48, 66), 13, 0.154),
        pair((329.63, 440.00), (52, 102), 14, 0.104),
        pair((466.16, 311.13), (54, 108), 14, 0.154),
    ),
    Variation(
        "09-pulse-round", "Pulse Round", "Full soft pulse with a longer finish", ("round", "calm"),
        (1.0, 0.16, 0.04), 5.5, 61, 0.006, 8, 3_700, 0.060, 1.08,
        pair((440.00, 554.37), (52, 72), 14, 0.158),
        pair((261.63, 349.23), (56, 118), 15, 0.106),
        pair((369.99, 246.94), (58, 124), 15, 0.158),
    ),
    Variation(
        "10-pulse-native", "Pulse Native", "Balanced pulse tuned for the AirType flow", ("balanced", "product"),
        (1.0, 0.13, 0.03), 3.8, 42, 0.010, 9, 4_000, 0.032, 1.10,
        pair((523.25, 659.25), (42, 60), 10, 0.160),
        pair((329.63, 440.00), (46, 94), 12, 0.108),
        pair((440.00, 293.66), (50, 102), 12, 0.160),
    ),
)


def one_pole_lowpass(samples: np.ndarray, cutoff_hz: float) -> np.ndarray:
    """Apply a zero-phase two-pass one-pole low-pass filter."""
    if not len(samples):
        return samples.copy()
    alpha = math.exp(-2.0 * math.pi * cutoff_hz / SAMPLE_RATE)
    coefficient = 1.0 - alpha

    def pass_filter(source: np.ndarray) -> np.ndarray:
        result = np.empty_like(source)
        previous = float(source[0])
        for index, value in enumerate(source):
            previous = coefficient * float(value) + alpha * previous
            result[index] = previous
        return result

    return pass_filter(pass_filter(samples)[::-1])[::-1]


def render_note(
    frequency: float,
    duration_ms: int,
    gain: float,
    variation: Variation,
    seed: int,
) -> np.ndarray:
    sample_count = max(1, round(SAMPLE_RATE * duration_ms / 1_000))
    time = np.arange(sample_count, dtype=np.float64) / SAMPLE_RATE
    settling = np.exp(-time / 0.012)
    cents = variation.pitch_drop_cents * settling
    instantaneous_frequency = frequency * np.power(2.0, cents / 1_200.0)
    phase = np.cumsum((2.0 * math.pi * instantaneous_frequency) / SAMPLE_RATE)

    body = np.zeros(sample_count, dtype=np.float64)
    for harmonic, weight in enumerate(variation.harmonics, start=1):
        body += weight * np.sin((phase * harmonic) + (0.11 * (harmonic - 1)))
    body /= sum(abs(weight) for weight in variation.harmonics)

    attack_seconds = variation.attack_ms / 1_000.0
    decay_seconds = variation.decay_ms / 1_000.0
    attack = 1.0 - np.exp(-time / max(attack_seconds, 0.0005))
    envelope = attack * np.exp(-time / max(decay_seconds, 0.001))

    release_count = min(sample_count, round(SAMPLE_RATE * 0.012))
    if release_count > 1:
        release = np.linspace(0.0, math.pi / 2.0, release_count)
        envelope[-release_count:] *= np.cos(release) ** 2

    rng = np.random.default_rng(seed)
    noise = rng.normal(0.0, 1.0, sample_count)
    noise = one_pole_lowpass(noise, min(variation.lowpass_hz, 3_200))
    noise -= one_pole_lowpass(noise, 180)
    noise_peak = float(np.max(np.abs(noise))) or 1.0
    noise /= noise_peak
    transient_envelope = attack * np.exp(-time / 0.0075)

    note = (body * envelope) + (variation.transient * noise * transient_envelope)
    return note * gain


def render_pair(spec: PairSpec, variation: Variation, event_index: int) -> np.ndarray:
    pre_roll = round(SAMPLE_RATE * 0.004)
    gap = round(SAMPLE_RATE * spec.gap_ms / 1_000)
    tail = round(SAMPLE_RATE * 0.014)
    note_lengths = [round(SAMPLE_RATE * duration / 1_000) for duration in spec.durations_ms]
    second_start = pre_roll + note_lengths[0] + gap
    total_length = second_start + note_lengths[1] + tail
    output = np.zeros(total_length, dtype=np.float64)

    first = render_note(
        spec.frequencies[0], spec.durations_ms[0], spec.gains[0], variation,
        seed=(event_index * 10_000) + (int(variation.id[:2]) * 100) + 1,
    )
    second = render_note(
        spec.frequencies[1], spec.durations_ms[1], spec.gains[1], variation,
        seed=(event_index * 10_000) + (int(variation.id[:2]) * 100) + 2,
    )
    output[pre_roll:pre_roll + len(first)] += first
    output[second_start:second_start + len(second)] += second

    if variation.reflection > 0:
        dry = output.copy()
        for delay_ms, level in ((13, variation.reflection), (27, variation.reflection * 0.38)):
            delay = round(SAMPLE_RATE * delay_ms / 1_000)
            output[delay:] += dry[:-delay] * level

    output = one_pole_lowpass(output, variation.lowpass_hz)
    output -= one_pole_lowpass(output, 38)
    output = np.tanh(output * variation.drive) / math.tanh(variation.drive)
    output -= float(np.mean(output))

    fade_count = min(len(output), round(SAMPLE_RATE * 0.008))
    if fade_count > 1:
        fade = np.linspace(0.0, math.pi / 2.0, fade_count)
        output[:fade_count] *= np.sin(fade) ** 2
        output[-fade_count:] *= np.cos(fade) ** 2

    observed_peak = float(np.max(np.abs(output))) or 1.0
    return output * (spec.target_peak / observed_peak)


def samples_to_wave_bytes(samples: np.ndarray) -> bytes:
    pcm = np.clip(np.rint(samples * 32_767.0), -32_768, 32_767).astype("<i2")
    buffer = BytesIO()
    with wave.open(buffer, "wb") as output:
        output.setnchannels(CHANNELS)
        output.setsampwidth(BITS_PER_SAMPLE // 8)
        output.setframerate(SAMPLE_RATE)
        output.writeframes(pcm.tobytes())
    return buffer.getvalue()


def read_wave_bytes(data: bytes) -> np.ndarray:
    with wave.open(BytesIO(data), "rb") as source:
        if (
            source.getnchannels() != CHANNELS
            or source.getsampwidth() != BITS_PER_SAMPLE // 8
            or source.getframerate() != SAMPLE_RATE
        ):
            raise ValueError("Baseline sound must be mono 44.1 kHz 16-bit PCM")
        frames = source.readframes(source.getnframes())
    return np.frombuffer(frames, dtype="<i2").astype(np.float64) / 32_768.0


def waveform_peaks(samples: np.ndarray) -> list[float]:
    peaks: list[float] = []
    for block in np.array_split(np.abs(samples), WAVEFORM_BINS):
        peaks.append(round(float(np.max(block)) if len(block) else 0.0, 3))
    return peaks


def sound_metadata(relative_path: str, data: bytes, samples: np.ndarray) -> dict[str, object]:
    windowed = samples * np.hanning(len(samples))
    magnitude = np.abs(np.fft.rfft(windowed))
    frequencies = np.fft.rfftfreq(len(samples), 1.0 / SAMPLE_RATE)
    dominant_hz = float(frequencies[int(np.argmax(magnitude))])
    magnitude_sum = float(np.sum(magnitude)) or 1.0
    centroid_hz = float(np.sum(frequencies * magnitude) / magnitude_sum)
    return {
        "path": relative_path,
        "durationMs": round(len(samples) * 1_000 / SAMPLE_RATE),
        "sha256": hashlib.sha256(data).hexdigest(),
        "rms": round(float(np.sqrt(np.mean(samples * samples))), 5),
        "dominantHz": round(dominant_hz),
        "centroidHz": round(centroid_hz),
        "peaks": waveform_peaks(samples),
    }


def find_previous_baseline(output_root: Path, event_name: str) -> Path:
    for directory_name in ("00-previous", "00-current"):
        candidate = output_root / "audio" / directory_name / f"{event_name}.wav"
        if candidate.is_file():
            return candidate
    raise FileNotFoundError(
        f"The private comparison baseline is missing for {event_name}: {output_root / 'audio'}"
    )


def build_assets(output_root: Path) -> tuple[dict[str, bytes], dict[str, object]]:
    files: dict[str, bytes] = {}
    themes: list[dict[str, object]] = []
    baseline_sounds: dict[str, object] = {}

    for event_name in EVENTS:
        source_path = find_previous_baseline(output_root, event_name)
        data = source_path.read_bytes()
        samples = read_wave_bytes(data)
        relative_path = f"audio/00-previous/{event_name}.wav"
        files[relative_path] = data
        baseline_sounds[event_name] = sound_metadata(relative_path, data, samples)

    themes.append({
        "id": "00-previous",
        "name": "Previous AirType",
        "character": "Preserved pre-selection sounds for historical comparison",
        "tags": ["baseline", "previous"],
        "sounds": baseline_sounds,
    })

    for variation in VARIATIONS:
        sounds: dict[str, object] = {}
        for event_index, event_name in enumerate(EVENTS, start=1):
            spec = getattr(variation, event_name)
            samples = render_pair(spec, variation, event_index)
            data = samples_to_wave_bytes(samples)
            relative_path = f"audio/{variation.id}/{event_name}.wav"
            metadata = sound_metadata(relative_path, data, samples)
            if event_name == "done" and int(metadata["dominantHz"]) > 470:
                raise ValueError(
                    f"{variation.name} Done is too high: {metadata['dominantHz']} Hz"
                )
            files[relative_path] = data
            sounds[event_name] = metadata

        themes.append({
            "id": variation.id,
            "name": variation.name,
            "character": variation.character,
            "tags": list(variation.tags),
            "sounds": sounds,
        })

    hashes = [hashlib.sha256(data).hexdigest() for data in files.values()]
    if len(hashes) != len(set(hashes)):
        raise ValueError("Every sound candidate must have a unique SHA-256 hash")

    catalog = {
        "schemaVersion": 2,
        "sampleRate": SAMPLE_RATE,
        "channels": CHANNELS,
        "bitsPerSample": BITS_PER_SAMPLE,
        "eventPreviewGain": {"start": 1.0, "done": 1.5, "cancel": 1.0},
        "themes": themes,
    }
    return files, catalog


def catalog_script(catalog: dict[str, object]) -> bytes:
    content = "window.AIRTYPE_SOUND_CATALOG = " + json.dumps(catalog, indent=2) + ";\n"
    return content.encode("utf-8")


def assert_safe_audio_root(output_root: Path) -> Path:
    resolved_output = output_root.resolve()
    audio_root = (resolved_output / "audio").resolve()
    if audio_root.parent != resolved_output or audio_root.name != "audio":
        raise ValueError(f"Refusing unsafe audio output path: {audio_root}")
    return audio_root


def generate(output_root: Path, verify: bool) -> None:
    files, catalog = build_assets(output_root)
    expected = {**files, "catalog.js": catalog_script(catalog)}
    audio_root = assert_safe_audio_root(output_root)

    if verify:
        for relative_path, expected_data in expected.items():
            path = output_root / relative_path
            if not path.is_file():
                raise FileNotFoundError(f"Missing generated asset: {path}")
            actual_data = path.read_bytes()
            if actual_data != expected_data:
                raise ValueError(f"Generated asset is stale: {path}")

        actual_audio_files = {
            path.relative_to(output_root).as_posix()
            for path in audio_root.rglob("*.wav")
        }
        if actual_audio_files != set(files):
            unexpected = sorted(actual_audio_files.symmetric_difference(files))
            raise ValueError(f"Generated audio set differs from catalog: {unexpected}")
    else:
        output_root.mkdir(parents=True, exist_ok=True)
        if audio_root.exists():
            shutil.rmtree(audio_root)
        for relative_path, data in expected.items():
            target = output_root / relative_path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)

    generated_done = [
        theme["sounds"]["done"]
        for theme in catalog["themes"][1:]
    ]
    done_range = (
        min(item["dominantHz"] for item in generated_done),
        max(item["dominantHz"] for item in generated_done),
    )
    action = "Verified" if verify else "Generated"
    print(f"{action} {len(files)} unique notification sound candidates.")
    print(f"Generated Done dominant-frequency range: {done_range[0]}-{done_range[1]} Hz.")
    print(f"Catalog: {output_root / 'catalog.js'}")


def verify_production() -> None:
    variations = {variation.id: variation for variation in VARIATIONS}
    for event_index, event_name in enumerate(EVENTS, start=1):
        variation_id, expected_hash = PRODUCTION_SELECTION[event_name]
        variation = variations[variation_id]
        spec = getattr(variation, event_name)
        generated = samples_to_wave_bytes(render_pair(spec, variation, event_index))
        generated_hash = hashlib.sha256(generated).hexdigest()
        if generated_hash != expected_hash:
            raise ValueError(
                f"Generated {event_name} hash changed: {generated_hash} != {expected_hash}"
            )

        production_path = PRODUCTION_SOUNDS / f"{event_name}.wav"
        production = production_path.read_bytes()
        if production != generated:
            raise ValueError(
                f"Production {event_name} does not match {variation.name}: {production_path}"
            )
        print(f"Verified {event_name}: {variation.name}, sha256={generated_hash}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--verify", action="store_true", help="Verify generated lab files are current")
    mode.add_argument(
        "--verify-production",
        action="store_true",
        help="Regenerate the approved cues in memory and verify AirType/Sounds",
    )
    parser.add_argument(
        "--output-root",
        type=Path,
        default=DEFAULT_OUTPUT_ROOT,
        help="Sound lab directory (defaults to design-assets/notification-sound-lab)",
    )
    return parser.parse_args()


if __name__ == "__main__":
    arguments = parse_args()
    if arguments.verify_production:
        verify_production()
    else:
        generate(arguments.output_root.resolve(), arguments.verify)
