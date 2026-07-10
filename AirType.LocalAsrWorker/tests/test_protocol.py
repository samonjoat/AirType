import pytest

from airtype_asr_worker.protocol import (
    PCM_HEADER_LENGTH,
    PROTOCOL_VERSION,
    ProtocolError,
    build_pcm_frame,
    parse_pcm_frame,
    parse_text_control,
)


def test_control_message_validation_accepts_protocol_v1_hello():
    message = parse_text_control('{"type":"hello","protocolVersion":1,"authToken":"secret"}')

    assert message["type"] == "hello"
    assert message["protocolVersion"] == PROTOCOL_VERSION


def test_control_message_validation_rejects_unknown_type():
    with pytest.raises(ProtocolError, match="unsupported control"):
        parse_text_control('{"type":"bogus","protocolVersion":1}')


def test_pcm_frame_parser_reads_little_endian_header_and_payload():
    payload = b"\x01\x00\x02\x00"
    raw = build_pcm_frame(sequence=7, first_sample=320, payload=payload)

    frame = parse_pcm_frame(raw)

    assert frame.sequence == 7
    assert frame.first_sample == 320
    assert frame.payload == payload
    assert frame.sample_count == 2
    assert raw[:4] == b"ATPC"
    assert raw[5] == PCM_HEADER_LENGTH


def test_pcm_frame_parser_rejects_bad_magic():
    raw = bytearray(build_pcm_frame(sequence=1, first_sample=0, payload=b"\x00\x00"))
    raw[0:4] = b"BAD!"

    with pytest.raises(ProtocolError, match="invalid magic"):
        parse_pcm_frame(bytes(raw))

