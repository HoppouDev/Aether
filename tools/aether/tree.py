"""Walking and comparing the src/ and .base/ trees."""

import filecmp
import os
from pathlib import Path

from . import paths

# Never part of the patched tree: committed docs, build output, patch leftovers.
SKIP_NAMES = {"AGENTS.md"}
SKIP_DIRS = {"bin", "obj"}
SKIP_SUFFIXES = (".rej", ".orig")


def is_tracked_path(rel: Path) -> bool:
    if rel.name in SKIP_NAMES or rel.name.endswith(SKIP_SUFFIXES):
        return False
    return not any(part in SKIP_DIRS for part in rel.parts[:-1])


def tree_files(root: Path) -> set[Path]:
    """Relative paths of all patchable files below root."""
    if not root.is_dir():
        return set()
    files = set()
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            rel = (Path(dirpath) / name).relative_to(root)
            if rel != Path(paths.STAMP.name) and is_tracked_path(rel):
                files.add(rel)
    return files


def is_binary(path: Path) -> bool:
    with open(path, "rb") as f:
        return b"\0" in f.read(8192)


def changed_files(src: Path, base: Path) -> tuple[list[Path], list[Path], list[Path]]:
    """Returns (modified, added, deleted) paths relative to the tree roots."""
    src_files = tree_files(src)
    base_files = tree_files(base)
    modified = sorted(
        p
        for p in src_files & base_files
        if not filecmp.cmp(src / p, base / p, shallow=False)
    )
    added = sorted(src_files - base_files)
    deleted = sorted(base_files - src_files)
    return modified, added, deleted
