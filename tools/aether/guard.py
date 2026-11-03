"""Pre-commit guard: game source, generated files and binaries never get committed."""

import hashlib
import subprocess
from pathlib import Path

from . import paths
from .tree import SKIP_NAMES, tree_files
from .util import run, sha256_file

# Staged paths with these extensions are refused outright.
FORBIDDEN_EXTENSIONS = {
    ".dll",
    ".exe",
    ".pdb",
    ".so",
    ".dylib",
    ".xnb",
    ".xwb",
    ".xsb",
    ".xgs",
}
# Staged files with these extensions are checked against .base/ for verbatim copies.
CONTENT_CHECK_EXTENSIONS = {".cs", ".json", ".tsv"}
GENERATED_DIRS = {".base", "lib", ".aether"}


def stage_patches() -> None:
    """Stages patches/ (new, changed and removed patches) for the commit."""
    tracked = run(
        ["git", "ls-files", "--", "patches"], cwd=paths.REPO, capture_output=True
    ).stdout.strip()
    # With zero patches there's no patches/ dir, and `git add` would fail on the pathspec.
    if paths.PATCHES.is_dir() or tracked:
        run(["git", "add", "-A", "--", "patches"], cwd=paths.REPO)


def staged_paths() -> list[str]:
    out = run(
        ["git", "diff", "--cached", "--name-only", "-z", "--diff-filter=ACMR"],
        cwd=paths.REPO,
        capture_output=True,
    ).stdout
    return [p for p in out.split("\0") if p]


def staged_problems() -> list[str]:
    """One line per staged file that must not be committed."""
    problems = []
    content_candidates = []
    for path in staged_paths():
        p = Path(path)
        top = p.parts[0]
        if top == "src" and p.name not in SKIP_NAMES:
            problems.append(
                f"{path}: game source is never committed; it's captured by patches/"
            )
        elif top in GENERATED_DIRS:
            problems.append(f"{path}: generated from the game, never committed")
        elif p.suffix.lower() in FORBIDDEN_EXTENSIONS:
            problems.append(
                f"{path}: binary/game asset type ({p.suffix}) is never committed"
            )
        elif top != "patches" and p.suffix.lower() in CONTENT_CHECK_EXTENSIONS:
            content_candidates.append(path)

    if content_candidates and paths.BASE.is_dir():
        base_hashes = {
            sha256_file(paths.BASE / rel)
            for rel in tree_files(paths.BASE)
            if rel.suffix.lower() in CONTENT_CHECK_EXTENSIONS
        }
        for path in content_candidates:
            blob = subprocess.run(
                ["git", "cat-file", "blob", f":{path}"],
                cwd=paths.REPO,
                capture_output=True,
                check=True,
            ).stdout
            if hashlib.sha256(blob).hexdigest() in base_hashes:
                problems.append(f"{path}: identical to a decompiled game file")
    return problems
