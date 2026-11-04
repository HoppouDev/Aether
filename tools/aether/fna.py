"""FNA, the XNA reimplementation the client runs on, pinned to one release (tools/fna.json).

The game ships an older FNA (26.3) with mismatched natives; Aether builds FNA itself from the
pinned commit and uses the matching native libraries (SDL3, FNA3D, FAudio, Theorafile) from
FNA's fnalibs build, so managed and native code come from the same release on every platform.
"""

import json
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
from .util import AetherError, log, run, sha256_file

FNA_REPO = "https://github.com/FNA-XNA/FNA.git"
FNA_PROJECT = "FNA.NetFramework.csproj"
FNA_OUTPUTS = ("FNA.dll", "FNA.dll.config")


def load_fna_json() -> dict:
    return json.loads(paths.FNA_JSON.read_text())


def build_fna(pinned: dict, dest: Path) -> None:
    """Builds FNA.dll (+ its dllmap config) from the pinned commit into dest, cached by commit."""
    commit = pinned["commit"]
    cache = paths.FNA_CACHE / commit
    out = cache / "bin" / "Release" / "net4.0"
    if not all((out / name).is_file() for name in FNA_OUTPUTS):
        if cache.exists():
            shutil.rmtree(cache)
        log(f"building FNA {pinned['version']} ({commit[:10]})…")
        cache.parent.mkdir(parents=True, exist_ok=True)
        run(
            [
                "git",
                "clone",
                "--quiet",
                "--depth",
                "1",
                "--branch",
                pinned["version"],
                "--recurse-submodules",
                "--shallow-submodules",
                FNA_REPO,
                str(cache),
            ],
            capture_output=True,
        )
        head = run(
            ["git", "rev-parse", "HEAD"], cwd=cache, capture_output=True
        ).stdout.strip()
        if head != commit:
            shutil.rmtree(cache)
            raise AetherError(
                f"FNA tag {pinned['version']} is {head[:10]}, but tools/fna.json pins {commit[:10]}."
            )
        run(
            ["dotnet", "build", FNA_PROJECT, "-c", "Release"],
            cwd=cache,
            capture_output=True,
        )
    for name in FNA_OUTPUTS:
        shutil.copyfile(out / name, dest / name)


def verify_files(root: Path, expected: dict[str, str]) -> list[str]:
    """Returns a problem line per missing, unexpected or mismatching file below root."""
    problems = []
    found = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
    for rel in sorted(set(expected) - found):
        problems.append(f"missing {rel}")
    for rel in sorted(found - set(expected)):
        problems.append(f"unexpected {rel}")
    for rel in sorted(set(expected) & found):
        if sha256_file(root / rel) != expected[rel]:
            problems.append(f"hash mismatch {rel}")
    return problems


def fetch_fnalibs(pinned: dict, dest: Path) -> None:
    """Downloads the pinned fnalibs build into dest and verifies every file's hash."""
    libs = pinned["fnalibs"]
    log(f"fetching FNA native libraries ({libs['repo']} run {libs['run_id']})…")
    with tempfile.TemporaryDirectory(prefix="aether-fnalibs-") as tmp:
        result = subprocess.run(
            [
                "gh",
                "run",
                "download",
                str(libs["run_id"]),
                "-R",
                libs["repo"],
                "-n",
                libs["artifact"],
                "-D",
                tmp,
            ],
            check=False,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            raise AetherError(
                "couldn't download the FNA native libraries with the GitHub CLI "
                "(needs `gh auth login`; CI artifacts also expire after 90 days):\n"
                + (result.stderr or result.stdout).strip()
            )
        problems = verify_files(Path(tmp), libs["files"])
        if problems:
            raise AetherError(
                "fnalibs don't match tools/fna.json:\n  " + "\n  ".join(problems)
            )
        shutil.copytree(tmp, dest)
