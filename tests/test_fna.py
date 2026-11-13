import hashlib
import json
import re
import subprocess
from pathlib import Path

from aether import fna
from aether.fna import verify_files

from .conftest import REAL_REPO, write_files


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def test_verify_files_accepts_an_exact_match(tmp_path):
    write_files(tmp_path, {"lib64/libFNA3D.so.0": b"fna3d", "README.txt": b"hi"})
    expected = {"lib64/libFNA3D.so.0": sha(b"fna3d"), "README.txt": sha(b"hi")}
    assert verify_files(tmp_path, expected) == []


def test_verify_files_reports_missing_unexpected_and_changed(tmp_path):
    write_files(tmp_path, {"lib64/libSDL3.so.0": b"tampered", "extra.dll": b"x"})
    expected = {"lib64/libSDL3.so.0": sha(b"original"), "x86/SDL3.dll": sha(b"y")}
    assert verify_files(tmp_path, expected) == [
        "missing x86/SDL3.dll",
        "unexpected extra.dll",
        "hash mismatch lib64/libSDL3.so.0",
    ]


def test_verify_files_expects_rebuilt_natives_instead_of_the_pins(tmp_path):
    write_files(tmp_path, {"lib64/libFNA3D.so.0": b"patched"})
    stamp = {"lib64/libFNA3D.so.0": sha(b"patched")}
    (tmp_path / fna.REBUILT_STAMP).write_text(json.dumps(stamp))
    expected = {"lib64/libFNA3D.so.0": sha(b"downloaded")}
    assert verify_files(tmp_path, expected) == []
    (tmp_path / "lib64/libFNA3D.so.0").write_bytes(b"changed since")
    assert verify_files(tmp_path, expected) == ["hash mismatch lib64/libFNA3D.so.0"]


def test_fetch_natives_only_refetches_stale_artifacts(tmp_path, monkeypatch):
    downloads = []

    def fake_run(cmd, **kwargs):
        artifact, dest = cmd[cmd.index("-n") + 1], Path(cmd[cmd.index("-D") + 1])
        downloads.append(artifact)
        write_files(dest, {"lib64/libFNA3D.so.0": b"new"})
        return subprocess.CompletedProcess(cmd, 0, "", "")

    monkeypatch.setattr(fna.subprocess, "run", fake_run)
    files = {"lib64/libFNA3D.so.0": sha(b"new")}
    pin = {
        "natives": {
            "repo": "o/r",
            "run_id": 1,
            "artifacts": {"current": files, "stale": files},
        }
    }
    write_files(tmp_path / "current", {"lib64/libFNA3D.so.0": b"new"})
    write_files(tmp_path / "stale", {"lib64/libFNA3D.so.0": b"old", "gone.so": b"x"})

    fna.fetch_natives(pin, tmp_path, only_stale=True)
    assert downloads == ["stale"]
    assert verify_files(tmp_path / "stale", files) == []

    fna.fetch_natives(pin, tmp_path, only_stale=True)
    assert downloads == ["stale"]


def test_fna_pin_is_well_formed():
    pin = json.loads((REAL_REPO / "tools" / "fna.json").read_text())
    assert re.fullmatch(r"\d+\.\d+", pin["version"])
    assert re.fullmatch(r"[0-9a-f]{40}", pin["commit"])
    artifacts = pin["natives"]["artifacts"]
    assert all(
        re.fullmatch(r"[0-9a-f]{64}", h) for a in artifacts.values() for h in a.values()
    )
    files, apple = artifacts["fnalibs"], artifacts["fnalibs-apple"]
    # Linux, 32-bit Windows and macOS are the run targets.
    for native in ("libSDL3.so.0", "libFNA3D.so.0", "libFAudio.so.0"):
        assert f"lib64/{native}" in files
    for native in ("SDL3.dll", "FNA3D.dll", "FAudio.dll"):
        assert f"x86/{native}" in files
    for native in ("libSDL3.0.dylib", "libFNA3D.0.dylib", "libFAudio.0.dylib"):
        assert f"osx/{native}" in apple
