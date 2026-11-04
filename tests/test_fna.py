import hashlib
import json
import re

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
