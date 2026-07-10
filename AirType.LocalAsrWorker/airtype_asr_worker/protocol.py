"""Protocol primitives for AirType's local ASR worker."""

from __future__ import annotations

from dataclasses import dataclass
import json
import struct
import time
from typing import Any, Mapping


PROTOCOL_VERSION = 1
PCM_MAGIC = b"ATPC"
PCM_HEADER_LENGTH = 24
PCM_HEADER_STRUCT = struct.Struct("<4sBBHQQ")

CONTROL_MESSAGE_TYPES = {
    "hello",
    "load_model",
    "prewarm",
    "start_recording",
    "stop_recording",
    "cancel_recording",
    "shutdown",
}


class ProtocolError(ValueError):
    """Raised when a control message or binary frame violates the protocol."""


@dataclass(frozen=True)
class PcmFrame:
    sequence: int
    first_sample: int
    payload: bytes
    reserved: int = 0

    @property
    def sample_count(self) -> int:
        return len(self.payload) // 2


def encode_json(payload: Mapping[str, Any]) -> str:
    return json.dumps(payload, separators=(",", ":"), ensure_ascii=False)


def decode_json_message(message: str) -> dict[str, Any]:
    try:
        payload = json.loads(message)
    except json.JSONDecodeError as exc:
        raise ProtocolError(f"invalid JSON control message: {exc.msg}") from exc

    if not isinstance(payload, dict):
        raise ProtocolError("control message must be a JSON object")
    return payload


def protocol_version(message: Mapping[str, Any]) -> int:
    raw_version = message.get("protocolVersion", message.get("version", PROTOCOL_VERSION))
    if raw_version != PROTOCOL_VERSION:
        raise ProtocolError(f"unsupported protocol version {raw_version!r}")
    return PROTOCOL_VERSION


def validate_control_message(message: Mapping[str, Any]) -> dict[str, Any]:
    protocol_version(message)

    message_type = message.get("type")
    if not isinstance(message_type, str) or not message_type:
        raise ProtocolError("control message requires a non-empty string 'type'")
    if message_type not in CONTROL_MESSAGE_TYPES:
        raise ProtocolError(f"unsupported control message type '{message_type}'")

    if message_type == "hello":
        token = message.get("authToken")
        if not isinstance(token, str) or not token:
            raise ProtocolError("hello requires a non-empty string 'authToken'")

    if message_type == "start_recording":
        hotwords = message.get("hotwords", [])
        if hotwords is not None and not isinstance(hotwords, (list, str)):
            raise ProtocolError("start_recording 'hotwords' must be a string, list, or null")

    normalized = dict(message)
    normalized["protocolVersion"] = PROTOCOL_VERSION
    return normalized


def parse_text_control(message: str) -> dict[str, Any]:
    return validate_control_message(decode_json_message(message))


def make_event(event_type: str, **payload: Any) -> dict[str, Any]:
    event = {
        "protocolVersion": PROTOCOL_VERSION,
        "type": event_type,
        "timestampUnixMs": int(time.time() * 1000),
    }
    event.update(payload)
    return event


def make_error_event(code: str, message: str, *, severity: str = "error", **payload: Any) -> dict[str, Any]:
    return make_event("error", code=code, message=message, severity=severity, **payload)


def parse_pcm_frame(data: bytes | bytearray | memoryview) -> PcmFrame:
    raw = bytes(data)
    if len(raw) < PCM_HEADER_LENGTH:
        raise ProtocolError(f"PCM frame is shorter than {PCM_HEADER_LENGTH} byte header")

    magic, version, header_length, reserved, sequence, first_sample = PCM_HEADER_STRUCT.unpack_from(raw)
    if magic != PCM_MAGIC:
        raise ProtocolError("PCM frame has invalid magic")
    if version != PROTOCOL_VERSION:
        raise ProtocolError(f"PCM frame has unsupported version {version}")
    if header_length != PCM_HEADER_LENGTH:
        raise ProtocolError(f"PCM frame has unsupported header length {header_length}")
    if len(raw) < header_length:
        raise ProtocolError("PCM frame is shorter than declared header length")

    payload = raw[header_length:]
    if len(payload) % 2:
        raise ProtocolError("PCM payload must contain 16-bit samples")

    return PcmFrame(sequence=sequence, first_sample=first_sample, payload=payload, reserved=reserved)


def build_pcm_frame(sequence: int, first_sample: int, payload: bytes, *, reserved: int = 0) -> bytes:
    if sequence < 0:
        raise ValueError("sequence must be non-negative")
    if first_sample < 0:
        raise ValueError("first_sample must be non-negative")
    if len(payload) % 2:
        raise ValueError("payload must contain 16-bit samples")

    header = PCM_HEADER_STRUCT.pack(
        PCM_MAGIC,
        PROTOCOL_VERSION,
        PCM_HEADER_LENGTH,
        reserved,
        sequence,
        first_sample,
    )
    return header + payload

