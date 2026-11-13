"""Repository layout."""

import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CLIENT = REPO / "client"  # tracked .NET projects
SRC = REPO / "src"  # editable tree: .base/ + patches/
BASE = REPO / ".base"  # pristine decompile
LIB = REPO / "lib"  # DLLs extracted from Terraria.exe, plus FNA.dll
PATCHES = REPO / "patches"
STATE = REPO / ".aether"
BASE_JSON = REPO / "tools" / "base.json"
FNA_JSON = REPO / "tools" / "fna.json"
# Aether's changes to FNA's code, applied before building it.
FNA_PATCH = REPO / "tools" / "fna.patch"
# Aether's changes to FNA3D (in FNA's checkout) and SDL: GPU frame timing, built on Linux.
FNA3D_PATCH = REPO / "tools" / "fna3d.patch"
SDL_PATCH = REPO / "tools" / "sdl.patch"
STEAMWORKS_JSON = REPO / "tools" / "steamworks.json"
# Self-contained builds per target (`aether build --target`); they contain game files.
DIST = REPO / "dist"
FNA_CACHE = STATE / "fna"  # FNA source checkout and build, reused across setups
NATIVES_CACHE = STATE / "natives"  # SDL checkout and the patched SDL3/FNA3D builds
# The pinned Linux build, fetched by `aether download`.
DOWNLOADED_GAME_DIR = STATE / "game"
# SteamCMD, installed by `aether download` if there's none.
STEAMCMD_DIR = STATE / "steamcmd"
# Scratch game folder `aether run` launches the client from, and its default saves.
RUN_DIR = STATE / "run"
SERVER_RUN_DIR = STATE / "server-run"
SERVER_SAVES_DIR = STATE / "server-saves"
SAVES_DIR = STATE / "saves"
STAMP = BASE / ".stamp"

# This OS's Steam install of Terraria (the Linux one is the base; the others only supply
# runtime files to `aether run`).
if os.name == "nt":
    DEFAULT_GAME_DIR = (
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
        / "Steam/steamapps/common/Terraria"
    )
elif sys.platform == "darwin":
    DEFAULT_GAME_DIR = (
        Path.home() / "Library/Application Support/Steam/steamapps/common/Terraria"
    )
else:
    DEFAULT_GAME_DIR = Path.home() / ".local/share/Steam/steamapps/common/Terraria"
