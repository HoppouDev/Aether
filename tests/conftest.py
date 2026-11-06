import json
import os
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

import pytest

from aether import patches, paths, pinning

REAL_REPO = Path(__file__).resolve().parents[1]

needs_diff_patch = pytest.mark.skipif(
    shutil.which("diff") is None or shutil.which("patch") is None,
    reason="GNU diff and patch are required",
)
needs_git = pytest.mark.skipif(shutil.which("git") is None, reason="git is required")


def write_files(root: Path, files: dict[str, str | bytes]) -> None:
    for rel, content in files.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        if isinstance(content, bytes):
            path.write_bytes(content)
        else:
            path.write_bytes(content.encode())


def read_tree(root: Path) -> dict[str, bytes]:
    return {
        p.relative_to(root).as_posix(): p.read_bytes()
        for p in sorted(root.rglob("*"))
        if p.is_file()
    }


def git(cwd: Path, *args: str) -> subprocess.CompletedProcess:
    # Keep the caller's git environment (e.g. GIT_INDEX_FILE inside a hook) out of temp repos.
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    env.update(
        GIT_AUTHOR_NAME="t",
        GIT_AUTHOR_EMAIL="t@example.com",
        GIT_COMMITTER_NAME="t",
        GIT_COMMITTER_EMAIL="t@example.com",
    )
    return subprocess.run(
        ["git", *args], cwd=cwd, env=env, check=True, capture_output=True, text=True
    )


@dataclass
class Repo:
    root: Path
    pinned: dict

    @property
    def src(self) -> Path:
        return paths.SRC

    @property
    def base(self) -> Path:
        return paths.BASE

    @property
    def patches(self) -> Path:
        return paths.PATCHES

    def make_base(
        self, files: dict[str, str | bytes], exe_sha256: str | None = None
    ) -> None:
        """Writes a pristine .base/ (with a valid stamp) and builds src/ from it."""
        write_files(self.base, files)
        pinning.write_stamp(
            self.base, exe_sha256 or self.pinned["exe_sha256"], self.pinned
        )
        assert patches.rebuild_src(force=True)

    def diff(self) -> list[str]:
        """What `aether diff` does: regenerate patches/ and record the sync state."""
        new_files = patches.regenerated_patch_set()
        changes = patches.sync_patches(new_files)
        patches.record_state(patches.digest_patch_set(new_files))
        return changes


@pytest.fixture
def repo(tmp_path, monkeypatch) -> Repo:
    """An isolated repo layout; every aether.paths constant points into it."""
    root = tmp_path / "repo"
    pinned = {
        "game_version": "test",
        "exe_sha256": "0" * 64,
        "ilspycmd": "0.0",
        "recipe": 1,
    }
    (root / "tools").mkdir(parents=True)
    (root / "tools" / "base.json").write_text(json.dumps(pinned))
    for name, rel in {
        "REPO": ".",
        "CLIENT": "client",
        "SRC": "src",
        "BASE": ".base",
        "LIB": "lib",
        "PATCHES": "patches",
        "STATE": ".aether",
        "DIST": "dist",
        "STEAMWORKS_JSON": "tools/steamworks.json",
        "BASE_JSON": "tools/base.json",
        "STAMP": ".base/.stamp",
    }.items():
        monkeypatch.setattr(
            paths, name, (root / rel).resolve() if rel == "." else root / rel
        )
    return Repo(root, pinned)
