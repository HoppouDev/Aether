"""FNA, the XNA reimplementation the client runs on, pinned to one release (tools/fna.json).

The game ships an older FNA (26.3) with mismatched natives; Aether builds FNA itself from the
pinned commit and uses the matching native libraries (SDL3, FNA3D, FAudio, Theorafile) from
FNA's fnalibs and fnalibs-apple builds, so managed and native code come from the same release on every platform.
Aether's few changes to FNA's code (tools/fna.patch) are applied to the checkout before building.
"""

import json
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
from .util import AetherError, log, run, sha256_file

FNA_REPO = "https://github.com/FNA-XNA/FNA.git"
FNA_PROJECT = "FNA.Core.csproj"
# Where FNA_PROJECT builds (its TargetFramework), relative to the FNA checkout.
FNA_OUTPUT_DIR = Path("bin") / "Release" / "net8.0"
FNA_OUTPUTS = ("FNA.dll", "FNA.dll.config")
# In the checkout: the hash of the tools/fna.patch it was built with.
FNA_PATCH_STAMP = ".aether-patch"


def load_fna_json() -> dict:
    return json.loads(paths.FNA_JSON.read_text())


def build_fna(pinned: dict, dest: Path) -> None:
    """Builds FNA.dll (+ its dllmap config) from the pinned commit and tools/fna.patch into dest.

    Cached by commit; a checkout built with another version of the patch is built again.
    """
    commit = pinned["commit"]
    cache = paths.FNA_CACHE / commit
    out = cache / FNA_OUTPUT_DIR
    stamp = cache / FNA_PATCH_STAMP
    patch = sha256_file(paths.FNA_PATCH)
    if (
        not all((out / name).is_file() for name in FNA_OUTPUTS)
        or not stamp.is_file()
        or stamp.read_text() != patch
    ):
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
        run(["git", "apply", str(paths.FNA_PATCH)], cwd=cache, capture_output=True)
        run(
            ["dotnet", "build", FNA_PROJECT, "-c", "Release", "-p:NuGetAudit=false"],
            cwd=cache,
            capture_output=True,
        )
        stamp.write_text(patch)
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


def fetch_natives(pinned: dict, lib: Path) -> None:
    """Downloads every pinned native-library package into lib/<artifact>/, hash-checked.

    `fnalibs` holds Linux and Windows (x86/x64) libraries, `fnalibs-apple` the macOS ones.
    """
    natives = pinned["natives"]
    log(f"fetching FNA native libraries ({natives['repo']} run {natives['run_id']})…")
    for artifact, files in natives["artifacts"].items():
        with tempfile.TemporaryDirectory(prefix="aether-fnalibs-") as tmp:
            result = subprocess.run(
                [
                    "gh",
                    "run",
                    "download",
                    str(natives["run_id"]),
                    "-R",
                    natives["repo"],
                    "-n",
                    artifact,
                    "-D",
                    tmp,
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            if result.returncode != 0:
                raise AetherError(
                    f"couldn't download {artifact} with the GitHub CLI "
                    "(needs `gh auth login`; CI artifacts also expire after 90 days):\n"
                    + (result.stderr or result.stdout).strip()
                )
            problems = verify_files(Path(tmp), files)
            if problems:
                raise AetherError(
                    f"{artifact} doesn't match tools/fna.json:\n  "
                    + "\n  ".join(problems)
                )
            shutil.copytree(tmp, lib / artifact)
