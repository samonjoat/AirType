import importlib.util
from pathlib import Path

import pytest


def _load_compat_module():
    shim_path = Path(__file__).parents[1] / "compat" / "av.py"
    spec = importlib.util.spec_from_file_location("airtype_av_compat_test", shim_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_av_compat_shim_is_marked_and_fails_closed():
    shim = _load_compat_module()

    assert shim.AIRTYPE_PCM_ONLY_SHIM is True
    with pytest.raises(RuntimeError, match="PCM-only Local ASR runtime"):
        _ = shim.open
