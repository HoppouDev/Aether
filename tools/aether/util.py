"""Small shared helpers."""

import hashlib
import os
import shutil
import subprocess
import sys
from pathlib import Path


class AetherError(Exception):
    """An expected failure, reported to the user without a traceback."""


def log(msg: str) -> None:
    print(msg, file=sys.stderr)


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    # Explicit UTF-8: on Windows the default would be the ANSI codepage.
    return subprocess.run(cmd, check=True, encoding="utf-8", errors="replace", **kwargs)


def require_executables(*names: str) -> None:
    missing = [n for n in names if shutil.which(n) is None]
    if missing:
        hint = (
            " On Windows they ship with Git for Windows (Git\\usr\\bin); aether.ps1 adds that to PATH."
            if os.name == "nt"
            else ""
        )
        raise AetherError(
            f"required tool(s) not found on PATH: {', '.join(missing)}.{hint}"
        )


def link_or_copy(src: Path, dst: Path) -> None:
    """Symlinks dst -> src, copying instead where symlinks aren't allowed (Windows without
    Developer Mode)."""
    try:
        dst.symlink_to(src)
    except OSError:
        shutil.copyfile(src, dst)


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def remove_empty_dirs(root: Path) -> None:
    """Removes empty directories below root (deepest first), keeping root itself."""
    if not root.is_dir():
        return
    for dirpath, _, _ in sorted(os.walk(root), key=lambda w: -len(w[0])):
        if Path(dirpath) != root and not os.listdir(dirpath):
            os.rmdir(dirpath)
