"""Hotword prompt token counting and truncation helpers."""

from __future__ import annotations

from dataclasses import dataclass
import re
from typing import Iterable


DEFAULT_MAX_HOTWORD_TOKENS = 100
_TOKEN_RE = re.compile(r"\w+|[^\w\s]", re.UNICODE)


@dataclass(frozen=True)
class HotwordPrompt:
    text: str
    token_count: int
    truncated: bool


def normalize_hotwords(hotwords: str | Iterable[object] | None) -> list[str]:
    if hotwords is None:
        return []
    if isinstance(hotwords, str):
        raw_items = hotwords.split(",")
    else:
        raw_items = [str(item) for item in hotwords]
    return [item.strip() for item in raw_items if item and item.strip()]


def _fallback_tokens(text: str) -> list[str]:
    return _TOKEN_RE.findall(text)


def build_hotword_prompt(
    hotwords: str | Iterable[object] | None,
    *,
    max_tokens: int = DEFAULT_MAX_HOTWORD_TOKENS,
    tokenizer: object | None = None,
) -> HotwordPrompt:
    if max_tokens <= 0:
        raise ValueError("max_tokens must be positive")

    text = ", ".join(normalize_hotwords(hotwords))
    if not text:
        return HotwordPrompt(text="", token_count=0, truncated=False)

    if tokenizer is not None:
        encoded = tokenizer.encode(text)
        ids = list(getattr(encoded, "ids", encoded))
        token_count = min(len(ids), max_tokens)
        truncated = len(ids) > max_tokens
        if not truncated:
            return HotwordPrompt(text=text, token_count=token_count, truncated=False)

    tokens = _fallback_tokens(text)
    truncated_tokens = tokens[:max_tokens]
    truncated = len(tokens) > max_tokens
    return HotwordPrompt(text=" ".join(truncated_tokens), token_count=len(truncated_tokens), truncated=truncated)

