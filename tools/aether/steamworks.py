"""The official 64-bit Windows Steamworks.NET, for running the client as a 64-bit process.

Terraria embeds the official Steamworks.NET 2024.8.0 builds unmodified (Linux/macOS, and
Windows x86 in its Windows build); the same release's Windows-x64 build and steam_api64.dll
are the matching 64-bit pair. Pinned by hash in tools/steamworks.json.
"""

import hashlib
import json
import tempfile
import urllib.request
import zipfile
from pathlib import Path

from . import paths
from .util import AetherError, log


def load_steamworks_json() -> dict:
    return json.loads(paths.STEAMWORKS_JSON.read_text())


def fetch_steamworks(lib: Path) -> None:
    """Downloads the pinned release and extracts its Windows-x64 files into lib/steamworks/."""
    pinned = load_steamworks_json()
    log(f"fetching Steamworks.NET {pinned['version']} (Windows x64)…")
    with tempfile.TemporaryDirectory(prefix="aether-steamworks-") as tmp:
        archive = Path(tmp) / "steamworks.zip"
        urllib.request.urlretrieve(pinned["url"], archive)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != pinned["sha256"]:
            raise AetherError(f"{pinned['url']} doesn't match tools/steamworks.json.")
        with zipfile.ZipFile(archive) as zf:
            for name, digest in pinned["files"].items():
                data = zf.read(name)
                if hashlib.sha256(data).hexdigest() != digest:
                    raise AetherError(
                        f"{name} in the Steamworks.NET release doesn't match."
                    )
                dest = lib / "steamworks" / name
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
