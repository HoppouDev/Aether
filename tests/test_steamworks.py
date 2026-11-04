import hashlib
import io
import json
import shutil
import zipfile

import pytest

from aether import paths, steamworks
from aether.util import AetherError

FILES = {
    "Windows-x64/Steamworks.NET.dll": b"steamworks x64",
    "Windows-x64/steam_api64.dll": b"steam x64",
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


@pytest.fixture
def release(repo, tmp_path, monkeypatch):
    """A fake Steamworks.NET release zip, pinned in tools/steamworks.json."""
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as zf:
        for name, data in {**FILES, "Windows-x86/steam_api.dll": b"x86"}.items():
            zf.writestr(name, data)
    archive = tmp_path / "release.zip"
    archive.write_bytes(buffer.getvalue())
    pinned = {
        "version": "1.0",
        "url": "https://example.invalid/release.zip",
        "sha256": sha256(archive.read_bytes()),
        "files": {name: sha256(data) for name, data in FILES.items()},
    }
    paths.STEAMWORKS_JSON.write_text(json.dumps(pinned))
    monkeypatch.setattr(
        steamworks.urllib.request,
        "urlretrieve",
        lambda url, dest: shutil.copy(archive, dest),
    )
    return pinned


def test_fetch_extracts_only_the_pinned_files(release):
    steamworks.fetch_steamworks(paths.LIB)
    out = paths.LIB / "steamworks"
    assert {p.relative_to(out).as_posix() for p in out.rglob("*.dll")} == set(FILES)
    for name, data in FILES.items():
        assert (out / name).read_bytes() == data


def test_fetch_rejects_a_different_archive(release):
    release["sha256"] = "0" * 64
    paths.STEAMWORKS_JSON.write_text(json.dumps(release))
    with pytest.raises(AetherError, match="doesn't match"):
        steamworks.fetch_steamworks(paths.LIB)
    assert not (paths.LIB / "steamworks").exists()


def test_fetch_rejects_a_different_file(release):
    release["files"]["Windows-x64/steam_api64.dll"] = "0" * 64
    paths.STEAMWORKS_JSON.write_text(json.dumps(release))
    with pytest.raises(AetherError, match="steam_api64.dll"):
        steamworks.fetch_steamworks(paths.LIB)
