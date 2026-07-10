"""Localhost WebSocket server for AirType ASR."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
import time
import uuid
from typing import Any

from .audio import AudioAccumulator
from .hotwords import HotwordPrompt, build_hotword_prompt
from .metrics import WorkerMetrics
from .protocol import (
    ProtocolError,
    encode_json,
    make_error_event,
    make_event,
    parse_pcm_frame,
    parse_text_control,
)
from .transcriber import MockTranscriber, Transcriber, create_transcriber
from .vad import MockVadSegmenter, SpeechSegment, create_vad_segmenter


LOCALHOST_NAMES = {"127.0.0.1", "localhost"}


@dataclass
class RecordingSession:
    recording_id: str
    hotword_prompt: HotwordPrompt
    prompt_token_cap: int
    accumulator: AudioAccumulator = field(default_factory=AudioAccumulator)
    vad: Any = field(default_factory=MockVadSegmenter)
    prompt_tail: str = ""
    streaming_segments_finalized: bool = False
    utterance_index: int = 0
    raw_text_parts: list[str] = field(default_factory=list)

    def note_frame(self, frame: Any) -> Any:
        return self.accumulator.add_frame(frame)

    def update_prompt_tail(self, raw_text: str) -> None:
        tokens = " ".join(raw_text.split()).split()
        if not tokens:
            self.prompt_tail = ""
            return
        self.prompt_tail = " ".join(tokens[-self.prompt_token_cap:])

    def current_prompt(self) -> str | None:
        return self.prompt_tail or None

    def note_utterance(self, raw_text: str) -> int:
        self.utterance_index += 1
        if raw_text:
            self.raw_text_parts.append(raw_text)
        return self.utterance_index

    def full_text(self) -> str:
        return " ".join(part for part in self.raw_text_parts if part).strip()


class AsrWorkerServer:
    def __init__(
        self,
        *,
        host: str = "127.0.0.1",
        port: int = 8765,
        auth_token: str,
        model_dir: str | None = None,
        cpu_threads: int = 8,
        transcriber: Transcriber | None = None,
        vad_segmenter: Any | None = None,
        allow_remote: bool = False,
    ) -> None:
        if not allow_remote and host not in LOCALHOST_NAMES:
            raise ValueError("ASR worker binds only to localhost unless allow_remote=True")
        if not auth_token:
            raise ValueError("auth_token is required")

        self.host = "127.0.0.1" if host == "localhost" else host
        self.port = port
        self.bound_port: int | None = None
        self.auth_token = auth_token
        self.model_dir = model_dir
        self.cpu_threads = cpu_threads
        self.transcriber = transcriber or create_transcriber(model_dir=model_dir, cpu_threads=cpu_threads)
        self._vad_segmenter = vad_segmenter
        self.metrics = WorkerMetrics()
        self.active_recording: RecordingSession | None = None
        self._server = None
        self._started: asyncio.Event | None = None
        self._stop_event: asyncio.Event | None = None
        self._recording_lock = asyncio.Lock()

    async def run_until_shutdown(self) -> None:
        try:
            import websockets  # type: ignore
        except Exception as exc:
            raise RuntimeError("websockets is required to run the ASR worker server") from exc

        self._started = asyncio.Event()
        self._stop_event = asyncio.Event()
        async with websockets.serve(self._handle_connection, self.host, self.port, max_size=None) as server:
            self._server = server
            sockets = getattr(server, "sockets", None) or []
            if sockets:
                self.bound_port = int(sockets[0].getsockname()[1])
            else:
                self.bound_port = self.port
            self._started.set()
            await self._stop_event.wait()

    async def wait_started(self, timeout_s: float = 5.0) -> None:
        deadline = asyncio.get_running_loop().time() + timeout_s
        while self._started is None:
            if asyncio.get_running_loop().time() >= deadline:
                raise TimeoutError("server did not start")
            await asyncio.sleep(0.01)
        await asyncio.wait_for(self._started.wait(), timeout=max(0.01, deadline - asyncio.get_running_loop().time()))

    async def stop(self) -> None:
        if self._stop_event is not None:
            self._stop_event.set()

    async def _handle_connection(self, websocket: Any, path: str | None = None) -> None:
        authenticated = False
        try:
            async for message in websocket:
                if isinstance(message, str):
                    try:
                        control = parse_text_control(message)
                    except ProtocolError as exc:
                        await self._send_error(websocket, "protocol_error", str(exc))
                        continue

                    if not authenticated:
                        authenticated = await self._authenticate(websocket, control)
                        if not authenticated:
                            return
                        continue

                    await self._handle_control(websocket, control)
                else:
                    if not authenticated:
                        await self._send_error(websocket, "auth_required", "hello must be accepted before binary audio")
                        await websocket.close(code=1008, reason="auth_required")
                        return
                    await self._handle_binary(websocket, bytes(message))
        finally:
            pass

    async def _authenticate(self, websocket: Any, control: dict[str, Any]) -> bool:
        if control["type"] != "hello":
            await self._send_error(websocket, "auth_required", "first control message must be hello")
            await websocket.close(code=1008, reason="auth_required")
            return False
        if control.get("authToken") != self.auth_token:
            await self._send_error(websocket, "auth_failed", "invalid auth token")
            await websocket.close(code=1008, reason="auth_failed")
            return False

        await self._send_event(
            websocket,
            "ready",
            host=self.host,
            port=self.bound_port or self.port,
            modelLoaded=self.metrics.model_loaded,
        )
        return True

    async def _handle_control(self, websocket: Any, control: dict[str, Any]) -> None:
        message_type = control["type"]
        if message_type == "hello":
            await self._send_error(websocket, "already_authenticated", "hello has already been accepted", severity="warning")
        elif message_type == "load_model":
            await self._load_model(websocket)
        elif message_type == "prewarm":
            await self._prewarm(websocket)
        elif message_type == "start_recording":
            await self._start_recording(websocket, control)
        elif message_type == "stop_recording":
            await self._stop_recording(websocket)
        elif message_type == "cancel_recording":
            await self._cancel_recording(websocket)
        elif message_type == "shutdown":
            await self._send_event(websocket, "recording_complete", canceled=True, reason="shutdown") if self.active_recording else None
            self.active_recording = None
            await self.stop()
        else:
            await self._send_error(websocket, "unsupported_control", f"unsupported control message {message_type}")

    async def _load_model(self, websocket: Any) -> None:
        await self._send_event(websocket, "model_loading")
        started = time.perf_counter()
        try:
            await asyncio.to_thread(self.transcriber.load_model)
        except Exception as exc:
            await self._send_error(websocket, "model_load_failed", str(exc))
            return
        self.metrics.model_loaded = True
        load_ms = int((time.perf_counter() - started) * 1000)
        await self._send_event(websocket, "model_loaded", loadMs=load_ms)

    async def _prewarm(self, websocket: Any) -> None:
        started = time.perf_counter()
        try:
            await asyncio.to_thread(self.transcriber.prewarm)
        except Exception as exc:
            await self._send_error(websocket, "prewarm_failed", str(exc))
            return
        prewarm_ms = int((time.perf_counter() - started) * 1000)
        await self._send_event(websocket, "prewarmed", prewarmMs=prewarm_ms)

    async def _start_recording(self, websocket: Any, control: dict[str, Any]) -> None:
        async with self._recording_lock:
            if self.active_recording is not None:
                await self._send_error(websocket, "recording_active", "only one recording can be active at a time")
                return

            hotword_token_cap = max(1, int(control.get("hotwordTokenCap") or 100))
            prompt_token_cap = max(1, int(control.get("initialPromptTokenCap") or 180))
            hotword_prompt = build_hotword_prompt(control.get("hotwords"), max_tokens=hotword_token_cap)
            recording_id = str(control.get("recordingId") or uuid.uuid4())
            vad = self._vad_segmenter or create_vad_segmenter(mock=isinstance(self.transcriber, MockTranscriber))
            if hasattr(vad, "reset"):
                vad.reset()
            self.active_recording = RecordingSession(
                recording_id=recording_id,
                hotword_prompt=hotword_prompt,
                prompt_token_cap=prompt_token_cap,
                vad=vad,
            )
            self.metrics.hotword_token_count = hotword_prompt.token_count

        await self._send_event(
            websocket,
            "recording_started",
            recordingId=recording_id,
            hotwordTokenCount=hotword_prompt.token_count,
            hotwordsTruncated=hotword_prompt.truncated,
        )
        await self._send_metrics(websocket)

    async def _handle_binary(self, websocket: Any, data: bytes) -> None:
        try:
            frame = parse_pcm_frame(data)
        except ProtocolError as exc:
            await self._send_error(websocket, "pcm_frame_invalid", str(exc))
            return

        session = self.active_recording
        if session is None:
            await self._send_error(websocket, "no_active_recording", "binary audio received without an active recording")
            return

        gap = session.note_frame(frame)
        self.metrics.note_frame(len(frame.payload))
        if gap is not None:
            self.metrics.note_sequence_warning()
            await self._send_error(
                websocket,
                "sequence_gap",
                f"expected PCM sequence {gap.expected}, received {gap.actual}",
                severity="warning",
                expected=gap.expected,
                actual=gap.actual,
            )

        try:
            segments = list(session.vad.accept_frame(frame))
        except Exception as exc:
            await self._send_error(websocket, "vad_failed", str(exc))
            return

        if segments:
            session.streaming_segments_finalized = True
            for segment in segments:
                await self._finalize_utterance(websocket, session, segment)

    async def _stop_recording(self, websocket: Any) -> None:
        async with self._recording_lock:
            session = self.active_recording
            self.active_recording = None

        if session is None:
            await self._send_error(websocket, "no_active_recording", "stop_recording received without an active recording")
            return

        flush_started = time.perf_counter()
        try:
            segments = list(session.vad.flush()) if hasattr(session.vad, "flush") else []
        except Exception as exc:
            await self._send_error(websocket, "vad_flush_failed", str(exc))
            segments = []

        if not segments and not session.streaming_segments_finalized and session.accumulator.total_bytes:
            segments = [SpeechSegment(pcm=session.accumulator.pcm_bytes(), first_sample=0, final=True)]

        for segment in segments:
            await self._finalize_utterance(websocket, session, segment)

        final_flush_ms = int((time.perf_counter() - flush_started) * 1000)
        await self._send_event(
            websocket,
            "recording_complete",
            recordingId=session.recording_id,
            text=session.full_text(),
            canceled=False,
            bytesReceived=session.accumulator.total_bytes,
            utteranceCount=session.utterance_index,
            utterancesFinal=self.metrics.utterances_final,
            finalFlushMs=final_flush_ms,
            asrTotalMs=self.metrics.asr_total_ms,
        )
        await self._send_metrics(websocket)

    async def _cancel_recording(self, websocket: Any) -> None:
        async with self._recording_lock:
            session = self.active_recording
            self.active_recording = None

        if session is None:
            await self._send_error(websocket, "no_active_recording", "cancel_recording received without an active recording", severity="warning")
            return
        if hasattr(session.vad, "reset"):
            session.vad.reset()
        await self._send_event(websocket, "recording_complete", recordingId=session.recording_id, canceled=True)
        await self._send_metrics(websocket)

    async def _finalize_utterance(self, websocket: Any, session: RecordingSession, segment: SpeechSegment) -> None:
        if not segment.pcm:
            return

        initial_prompt = session.current_prompt()
        try:
            started = time.perf_counter()
            result = await asyncio.to_thread(
                self.transcriber.transcribe_pcm16,
                segment.pcm,
                initial_prompt=initial_prompt,
                hotwords=session.hotword_prompt.text or None,
            )
        except Exception as exc:
            await self._send_error(websocket, "transcription_failed", str(exc))
            return

        asr_ms = int((time.perf_counter() - started) * 1000)
        self.metrics.note_utterance()
        self.metrics.note_asr(asr_ms)
        raw_text = result.raw_text or result.text
        index = session.note_utterance(raw_text)
        session.update_prompt_tail(raw_text)
        start_ms = int(segment.first_sample * 1000 / 16000)
        end_ms = int((segment.first_sample + (len(segment.pcm) // 2)) * 1000 / 16000)
        await self._send_event(
            websocket,
            "utterance_final",
            recordingId=session.recording_id,
            index=index,
            text=result.text,
            rawText=raw_text,
            startMs=start_ms,
            endMs=end_ms,
            asrMs=asr_ms,
            language=result.language,
            durationSeconds=result.duration_s,
            firstSample=segment.first_sample,
        )

    async def _send_metrics(self, websocket: Any) -> None:
        await self._send_event(websocket, "metrics", **self.metrics.snapshot(active_recording=self.active_recording is not None))

    async def _send_event(self, websocket: Any, event_type: str, **payload: Any) -> None:
        await websocket.send(encode_json(make_event(event_type, **payload)))

    async def _send_error(
        self,
        websocket: Any,
        code: str,
        message: str,
        *,
        severity: str = "error",
        **payload: Any,
    ) -> None:
        await websocket.send(encode_json(make_error_event(code, message, severity=severity, **payload)))
