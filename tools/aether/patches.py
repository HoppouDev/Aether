"""Generating patches/ from src/, and rebuilding src/ from .base/ + patches/."""

import concurrent.futures
import filecmp
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
from .tree import (
    SKIP_DIRS,
    SKIP_NAMES,
    SKIP_SUFFIXES,
    changed_files,
    is_binary,
    tree_files,
)
from .util import AetherError, log, remove_empty_dirs


def state_file():
    return paths.STATE / "state.json"


# ---------------------------------------------------------------------------
# Generating


def make_patch(rel: Path, old: Path | None, new: Path | None, empty: Path) -> bytes:
    """Unified diff of old -> new; None means the file doesn't exist on that side.

    A missing side is diffed as the empty file `empty` (os.devnull isn't portable to Windows'
    GNU diff) and labelled /dev/null, which is what makes `patch` create or delete the file.
    Output stays bytes so patches are written exactly as diff produced them.
    """
    old_label = f"a/{rel.as_posix()}" if old else "/dev/null"
    new_label = f"b/{rel.as_posix()}" if new else "/dev/null"
    # src/ isn't tracked by git, so nothing normalizes line endings there: ignore a trailing
    # CR so a Windows editor saving CRLF doesn't turn a one-line edit into a whole-file patch.
    result = subprocess.run(
        [
            "diff",
            "-u",
            "--strip-trailing-cr",
            "--label",
            old_label,
            "--label",
            new_label,
            str(old or empty),
            str(new or empty),
        ],
        check=False,
        capture_output=True,
    )
    if result.returncode not in (0, 1):
        raise AetherError(
            f"diff failed for {rel}: {result.stderr.decode(errors='replace').strip()}"
        )
    return result.stdout


Changes = tuple[list[Path], list[Path], list[Path]]


def generate_patches(out_dir: Path, changes: Changes | None = None) -> None:
    """Writes one .patch per file that differs between src/ and .base/ into out_dir.

    changes: a precomputed changed_files(paths.SRC, paths.BASE) result, to avoid comparing the trees twice.
    """
    modified, added, deleted = changes or changed_files(paths.SRC, paths.BASE)
    binary = [p for p in modified + added if is_binary(paths.SRC / p)]
    if binary:
        listing = "\n  ".join(str(p) for p in binary)
        raise AetherError(
            f"binary files differ from .base/ and can't be patched:\n  {listing}"
        )

    jobs: list[tuple[Path, Path | None, Path | None]] = [
        (p, paths.BASE / p, paths.SRC / p) for p in modified
    ]
    jobs += [(p, None, paths.SRC / p) for p in added]
    jobs += [(p, paths.BASE / p, None) for p in deleted]

    with tempfile.TemporaryDirectory(prefix="aether-empty-") as tmp:
        empty = Path(tmp) / "empty"
        empty.touch()
        with concurrent.futures.ThreadPoolExecutor() as pool:
            for (rel, _, _), data in zip(
                jobs, pool.map(lambda job: make_patch(*job, empty), jobs)
            ):
                if not data:  # differs only in line endings
                    continue
                target = out_dir / f"{rel.as_posix()}.patch"
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)


def patches_version_file() -> Path:
    """Records the game version patches/ is known to apply to (committed with the patches)."""
    return paths.PATCHES / "GAME_VERSION"


def patches_game_version() -> str | None:
    path = patches_version_file()
    return path.read_text().strip() if path.is_file() else None


def mark_patches_compatible(version: str) -> bool:
    """Records that patches/ applies to version; returns whether that changed anything."""
    if patches_game_version() == version:
        return False
    paths.PATCHES.mkdir(parents=True, exist_ok=True)
    patches_version_file().write_text(version + "\n")
    return True


def patch_files(root: Path) -> dict[Path, bytes]:
    if not root.is_dir():
        return {}
    return {p.relative_to(root): p.read_bytes() for p in root.rglob("*.patch")}


def regenerated_patch_set(changes: Changes | None = None) -> dict[Path, bytes]:
    """The patch set src/ currently corresponds to, without touching patches/."""
    with tempfile.TemporaryDirectory(prefix="aether-diff-") as tmp:
        generate_patches(Path(tmp), changes)
        return patch_files(Path(tmp))


def digest_patch_set(files: dict[Path, bytes]) -> str:
    h = hashlib.sha256()
    for rel in sorted(files):
        h.update(rel.as_posix().encode() + b"\0" + files[rel] + b"\0")
    return h.hexdigest()


def sync_patches(new_files: dict[Path, bytes]) -> list[str]:
    """Makes patches/ contain exactly new_files. Returns one line per change."""
    changes = []
    old_files = patch_files(paths.PATCHES)
    for rel, data in sorted(new_files.items()):
        if old_files.get(rel) != data:
            path = paths.PATCHES / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            changes.append(
                f"{'updated' if rel in old_files else 'created'} patches/{rel.as_posix()}"
            )
    for rel in sorted(set(old_files) - set(new_files)):
        (paths.PATCHES / rel).unlink()
        changes.append(f"removed patches/{rel.as_posix()}")
    remove_empty_dirs(paths.PATCHES)
    return changes


# ---------------------------------------------------------------------------
# Sync state: the patch set src/ matched when it was last synced with patches/,
# so `apply` can tell "src/ is unchanged since then" from "src/ has new edits".


def record_state(patch_digest: str) -> None:
    paths.STATE.mkdir(exist_ok=True)
    state_file().write_text(
        json.dumps({"src_patch_digest": patch_digest}, indent=2) + "\n"
    )


def recorded_digest() -> str | None:
    if not state_file().is_file():
        return None
    return json.loads(state_file().read_text()).get("src_patch_digest")


# ---------------------------------------------------------------------------
# Applying


def reset_src_to_base() -> None:
    """Makes src/ identical to .base/, keeping AGENTS.md files and build output."""
    paths.SRC.mkdir(exist_ok=True)
    base_files = tree_files(paths.BASE)
    for rel in base_files:
        dst = paths.SRC / rel
        if not dst.is_file() or not filecmp.cmp(paths.BASE / rel, dst, shallow=False):
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(paths.BASE / rel, dst)
    for dirpath, dirnames, filenames in os.walk(paths.SRC):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            path = Path(dirpath) / name
            if name in SKIP_NAMES:
                continue
            if (
                name.endswith(SKIP_SUFFIXES)
                or path.relative_to(paths.SRC) not in base_files
            ):
                path.unlink()
    remove_empty_dirs(paths.SRC)


def apply_patches() -> tuple[list[str], list[str]]:
    """Applies patches/ to src/. Returns (failed, inexact) relative paths."""
    failed, inexact = [], []
    for patch in (
        sorted(paths.PATCHES.rglob("*.patch")) if paths.PATCHES.is_dir() else []
    ):
        rel = patch.relative_to(paths.PATCHES).as_posix()[: -len(".patch")]
        reject = paths.SRC / f"{rel}.rej"
        result = subprocess.run(
            [
                "patch",
                "-p1",
                "-d",
                str(paths.SRC),
                "--forward",
                "--batch",
                "--no-backup-if-mismatch",
                "-r",
                str(reject),
                "-i",
                str(patch),
            ],
            check=False,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
        )
        if result.returncode != 0:
            failed.append(rel)
            log(f"FAILED {rel}\n{result.stdout.strip()}{result.stderr.strip()}")
        elif re.search(r"with fuzz|offset", result.stdout):
            inexact.append(rel)
            log(f"applied with offset/fuzz: {rel}")
    return failed, inexact


def ensure_src_captured() -> None:
    """Fails if src/ has edits (relative to the current .base/) that patches/ lacks.

    "Captured" means src/ is unchanged since it was last synced with patches/, or it
    already matches patches/ (e.g. patches/ changed upstream, src/ was never edited).
    """
    if not (paths.SRC.is_dir() and tree_files(paths.SRC)):
        return
    current = digest_patch_set(regenerated_patch_set())
    if current not in (recorded_digest(), digest_patch_set(patch_files(paths.PATCHES))):
        raise AetherError(
            "src/ has edits that aren't captured in patches/ and would be lost.\n"
            "Run `./aether diff` to keep them, or pass --force to discard them."
        )


def rebuild_src(force: bool) -> bool:
    """Resets src/ to .base/ + patches/. Returns False if any patch failed to apply."""
    if not force:
        ensure_src_captured()
    reset_src_to_base()
    failed, inexact = apply_patches()
    record_state(digest_patch_set(regenerated_patch_set()))
    total = len(patch_files(paths.PATCHES))
    print(f"applied {total - len(failed)}/{total} patch(es) to src/.")
    if inexact:
        print("Some patches needed offset/fuzz; run `./aether diff` to refresh them.")
    if failed:
        print(
            "Resolve the conflicts recorded in the src/**/*.rej files, then run `./aether diff`."
        )
    return not failed
