"""Aether's allocation profiler (profiler/aether_profiler.c): built here, loaded by the runtime.

`aether run --profile` (also `launch`, `screens`) starts the client with it; the game's control
server then records allocation call stacks on request (`profile [FRAMES] [TOP] PATH`, or the MCP
server's `game_profile`). It's a CoreCLR profiler (ICorProfilerCallback) that .NET loads at startup
from the CORECLR_PROFILER* variables; Linux only, since it's built here with `cc`.
"""

from pathlib import Path

from . import paths
from .util import AetherError, log, require_executables, run

SOURCE = Path(__file__).parent / "profiler" / "aether_profiler.c"
LIBRARY = paths.STATE / "profiler" / "libaether-profiler.so"
# The profiler's class id (CLSID_AetherProfiler in the source).
CLSID = "{9A1E2F5C-3B7D-4E8A-A6C2-5D0F1B3E7A41}"


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
            "-std=gnu11",
            "-shared",
            "-fPIC",
            "-fvisibility=hidden",
            "-O2",
            "-o",
            str(LIBRARY),
            str(SOURCE),
            "-lpthread",
        ]
    )
    return LIBRARY


def profiler_env(platform: str, depth: int) -> dict[str, str]:
    """The environment that has .NET load the profiler, keeping depth frames per stack."""
    if platform != "linux":
        raise AetherError("--profile is Linux only (the profiler is built with cc).")
    if not 1 <= depth <= 32:
        raise AetherError("--profile-depth must be between 1 and 32.")
    library = build_profiler()
    return {
        "CORECLR_ENABLE_PROFILING": "1",
        "CORECLR_PROFILER": CLSID,
        "CORECLR_PROFILER_PATH": str(library),
        "AETHER_PROFILER": f"depth={depth}",
    }
