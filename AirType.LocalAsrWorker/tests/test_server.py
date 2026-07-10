import asyncio
import json

import pytest

websockets = pytest.importorskip("websockets")

from airtype_asr_worker.protocol import build_pcm_frame
from airtype_asr_worker.server import AsrWorkerServer
from airtype_asr_worker.transcriber import MockTranscriber
from airtype_asr_worker.vad import SpeechSegment


AUTH_TOKEN = "test-token"


async def recv_event(websocket, event_type=None):
    while True:
        event = json.loads(await asyncio.wait_for(websocket.recv(), timeout=2))
        if event_type is None or event["type"] == event_type:
            return event


async def start_server(*, transcriber=None, vad_segmenter=None):
    server = AsrWorkerServer(
        host="127.0.0.1",
        port=0,
        auth_token=AUTH_TOKEN,
        transcriber=transcriber or MockTranscriber(),
        vad_segmenter=vad_segmenter,
    )
    task = asyncio.create_task(server.run_until_shutdown())
    await server.wait_started()
    return server, task


async def stop_server(server, task):
    await server.stop()
    await asyncio.wait_for(task, timeout=2)


async def connect_authenticated(server):
    websocket = await websockets.connect(f"ws://127.0.0.1:{server.bound_port}", max_size=None)
    await websocket.send(json.dumps({"type": "hello", "protocolVersion": 1, "authToken": AUTH_TOKEN}))
    ready = await recv_event(websocket, "ready")
    assert ready["host"] == "127.0.0.1"
    return websocket


def test_localhost_default_binding_configuration():
    server = AsrWorkerServer(port=0, auth_token=AUTH_TOKEN, transcriber=MockTranscriber())

    assert server.host == "127.0.0.1"


def test_remote_binding_rejected_by_default():
    with pytest.raises(ValueError, match="localhost"):
        AsrWorkerServer(host="0.0.0.0", port=0, auth_token=AUTH_TOKEN, transcriber=MockTranscriber())


@pytest.mark.asyncio
async def test_auth_rejection_closes_connection():
    server, task = await start_server()
    try:
        websocket = await websockets.connect(f"ws://127.0.0.1:{server.bound_port}", max_size=None)
        await websocket.send(json.dumps({"type": "hello", "protocolVersion": 1, "authToken": "wrong"}))

        error = await recv_event(websocket, "error")

        assert error["code"] == "auth_failed"
        with pytest.raises(websockets.ConnectionClosed):
            await websocket.recv()
    finally:
        await stop_server(server, task)


@pytest.mark.asyncio
async def test_recording_lifecycle_start_frame_stop_and_cancel_with_mock_transcriber():
    transcriber = MockTranscriber(["hello world"])
    server, task = await start_server(transcriber=transcriber)
    try:
        websocket = await connect_authenticated(server)
        await websocket.send(json.dumps({"type": "start_recording", "protocolVersion": 1, "recordingId": "rec-1"}))
        started = await recv_event(websocket, "recording_started")
        assert started["recordingId"] == "rec-1"

        await websocket.send(build_pcm_frame(sequence=0, first_sample=0, payload=b"\x00\x00" * 160))
        await websocket.send(json.dumps({"type": "stop_recording", "protocolVersion": 1}))

        final = await recv_event(websocket, "utterance_final")
        complete = await recv_event(websocket, "recording_complete")

        assert final["text"] == "hello world"
        assert complete["recordingId"] == "rec-1"
        assert complete["canceled"] is False

        await websocket.send(json.dumps({"type": "start_recording", "protocolVersion": 1, "recordingId": "rec-2"}))
        await recv_event(websocket, "recording_started")
        await websocket.send(json.dumps({"type": "cancel_recording", "protocolVersion": 1}))
        canceled = await recv_event(websocket, "recording_complete")
        assert canceled["recordingId"] == "rec-2"
        assert canceled["canceled"] is True
        await websocket.close()
    finally:
        await stop_server(server, task)


@pytest.mark.asyncio
async def test_metrics_event_emits_hotword_token_count():
    server, task = await start_server()
    try:
        websocket = await connect_authenticated(server)
        await websocket.send(
            json.dumps(
                {
                    "type": "start_recording",
                    "protocolVersion": 1,
                    "recordingId": "rec-hotwords",
                    "hotwords": ["alpha", "beta", "gamma"],
                }
            )
        )
        await recv_event(websocket, "recording_started")
        metrics = await recv_event(websocket, "metrics")

        assert metrics["hotwordTokenCount"] == 5
        assert metrics["activeRecording"] is True
        await websocket.close()
    finally:
        await stop_server(server, task)


class FrameFinalizingVad:
    def reset(self):
        pass

    def accept_frame(self, frame):
        return [SpeechSegment(pcm=frame.payload, first_sample=frame.first_sample)]

    def flush(self):
        return []


@pytest.mark.asyncio
async def test_prompt_tail_resets_per_recording_and_uses_current_recording_raw_tail_only():
    transcriber = MockTranscriber(["first raw text", "second raw text", "new recording raw text"])
    server, task = await start_server(transcriber=transcriber, vad_segmenter=FrameFinalizingVad())
    try:
        websocket = await connect_authenticated(server)
        await websocket.send(json.dumps({"type": "start_recording", "protocolVersion": 1, "recordingId": "rec-prompts"}))
        await recv_event(websocket, "recording_started")
        await websocket.send(build_pcm_frame(sequence=0, first_sample=0, payload=b"\x01\x00" * 10))
        await recv_event(websocket, "utterance_final")
        await websocket.send(build_pcm_frame(sequence=1, first_sample=10, payload=b"\x02\x00" * 10))
        await recv_event(websocket, "utterance_final")
        await websocket.send(json.dumps({"type": "stop_recording", "protocolVersion": 1}))
        await recv_event(websocket, "recording_complete")

        await websocket.send(json.dumps({"type": "start_recording", "protocolVersion": 1, "recordingId": "rec-prompts-2"}))
        await recv_event(websocket, "recording_started")
        await websocket.send(build_pcm_frame(sequence=0, first_sample=0, payload=b"\x03\x00" * 10))
        await recv_event(websocket, "utterance_final")

        assert transcriber.prompts == [None, "first raw text", None]
        await websocket.close()
    finally:
        await stop_server(server, task)


@pytest.mark.asyncio
async def test_sequence_gap_emits_warning_error_event_and_continues():
    server, task = await start_server()
    try:
        websocket = await connect_authenticated(server)
        await websocket.send(json.dumps({"type": "start_recording", "protocolVersion": 1, "recordingId": "rec-gap"}))
        await recv_event(websocket, "recording_started")

        await websocket.send(build_pcm_frame(sequence=0, first_sample=0, payload=b"\x00\x00" * 10))
        await websocket.send(build_pcm_frame(sequence=2, first_sample=20, payload=b"\x00\x00" * 10))
        warning = await recv_event(websocket, "error")

        assert warning["code"] == "sequence_gap"
        assert warning["severity"] == "warning"
        assert warning["expected"] == 1
        assert warning["actual"] == 2
        await websocket.send(json.dumps({"type": "stop_recording", "protocolVersion": 1}))
        complete = await recv_event(websocket, "recording_complete")
        assert complete["canceled"] is False
        await websocket.close()
    finally:
        await stop_server(server, task)

