"""Command line entry point for the AirType local ASR worker."""

from __future__ import annotations

import argparse
import asyncio

from .server import AsrWorkerServer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run the AirType local ASR worker.")
    parser.add_argument("--host", default="127.0.0.1", help="Bind host. Defaults to 127.0.0.1.")
    parser.add_argument("--port", type=int, required=True, help="Bind port.")
    parser.add_argument("--auth-token", required=True, help="Shared auth token required in the hello message.")
    parser.add_argument("--model-dir", default=None, help="Directory used by faster-whisper for model files.")
    parser.add_argument("--cpu-threads", type=int, default=8, help="CPU threads for faster-whisper.")
    parser.add_argument("--mock-transcriber", action="store_true", help="Use deterministic mock transcription.")
    return parser


async def _run(args: argparse.Namespace) -> None:
    server = AsrWorkerServer(
        host=args.host,
        port=args.port,
        auth_token=args.auth_token,
        model_dir=args.model_dir,
        cpu_threads=args.cpu_threads,
        transcriber=None,
    )
    if args.mock_transcriber:
        from .transcriber import MockTranscriber

        server.transcriber = MockTranscriber()
    await server.run_until_shutdown()


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    asyncio.run(_run(args))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

