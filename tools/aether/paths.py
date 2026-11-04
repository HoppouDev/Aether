"""Repository layout."""

import os
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SRC = REPO / "src"  # editable tree: .base/ + patches/
BASE = REPO / ".base"  # pristine decompile
LIB = REPO / "lib"  # DLLs extracted from Terraria.exe, plus FNA.dll
PATCHES = REPO / "patches"
STATE = REPO / ".aether"
BASE_JSON = REPO / "tools" / "base.json"
FNA_JSON = REPO / "tools" / "fna.json"
FNA_CACHE = STATE / "fna"  # FNA source checkout and build, reused across setups
STAMP = BASE / ".stamp"

DEFAULT_GAME_DIR = (
    Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"))
    / "Steam/steamapps/common/Terraria"
    if os.name == "nt"
    else Path.home() / ".local/share/Steam/steamapps/common/Terraria"
)
