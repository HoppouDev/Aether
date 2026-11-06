"""Aether's allocation profiler (profiler/aether_profiler.c): built here, preloaded into the game.

`aether run --profile` (also `launch`, `screens`) starts the client with it; the game's control
server then records allocation call stacks on request (`profile [FRAMES] [TOP] PATH`, or the MCP
server's `game_profile`). Linux only: it's a Mono profiler preloaded into MonoKickstart's launcher.
"""

import os
from pathlib import Path

from . import paths
from .util import AetherError, log, require_executables, run

SOURCE = Path(__file__).parent / "profiler" / "aether_profiler.c"
LIBRARY = paths.STATE / "profiler" / "libmono-profiler-aether.so"


def build_profiler() -> Path:
    """Compiles the profiler into .aether/profiler/ if it's missing or older than its source."""
    if LIBRARY.exists() and LIBRARY.stat().st_mtime >= SOURCE.stat().st_mtime:
        return LIBRARY
    require_executables("cc")
    LIBRARY.parent.mkdir(parents=True, exist_ok=True)
    log("building the allocation profiler…")
    run(
        [
            "cc",
            "-shared",
            "-fPIC",
            "-O2",
            "-o",
            str(LIBRARY),
            str(SOURCE),
            "-lpthread",
            "-ldl",
        ]
    )
    return LIBRARY


def profiler_env(platform: str, depth: int) -> dict[str, str]:
    """The environment that preloads the profiler into the client, keeping depth frames per stack."""
    raise AetherError(
        "--profile isn't available yet: the allocation profiler is being ported from Mono to "
        ".NET 10's runtime."
    )
    if platform != "linux":
        raise AetherError(
            "--profile is Linux only (it's preloaded into the Linux launcher)."
        )
    if not 1 <= depth <= 32:
        raise AetherError("--profile-depth must be between 1 and 32.")
    library = build_profiler()
    preload = os.environ.get("LD_PRELOAD")
    return {
        "AETHER_PROFILER": f"depth={depth}",
        "LD_PRELOAD": f"{library}:{preload}" if preload else str(library),
    }
