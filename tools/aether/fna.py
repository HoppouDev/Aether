"""FNA, the XNA reimplementation the client runs on, pinned to one release (tools/fna.json).

The game ships an older FNA (26.3) with mismatched natives; Aether builds FNA itself from the
pinned commit and uses the matching native libraries (SDL3, FNA3D, FAudio, Theorafile) from
FNA's fnalibs and fnalibs-apple builds, so managed and native code come from the same release on every platform.
Aether's few changes to FNA's code (tools/fna.patch) are applied to the checkout before building.
On Linux, SDL3 and FNA3D are also built from source with Aether's patches (tools/sdl.patch,
tools/fna3d.patch: GPU frame timing, FNA3D_GetGPUFrameTimeEXT) over the downloaded ones.
"""

import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from . import paths
from .util import AetherError, log, run, sha256_file

FNA_REPO = "https://github.com/FNA-XNA/FNA.git"
FNA_PROJECT = "FNA.Core.csproj"
# Where FNA_PROJECT builds (its TargetFramework), relative to the FNA checkout.
FNA_OUTPUT_DIR = Path("bin") / "Release" / "net8.0"
FNA_OUTPUTS = ("FNA.dll", "FNA.dll.config")
# In the checkout: the hash of the tools/fna.patch (and fna3d.patch) it was built with.
FNA_PATCH_STAMP = ".aether-patch"
# The natives built from source (build_natives), each with its hash, in lib/<artifact>/: these
# replace downloaded files, so they're expected there instead of the pinned hashes.
REBUILT_STAMP = ".aether-rebuilt"


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
    patch = sha256_file(paths.FNA_PATCH) + sha256_file(paths.FNA3D_PATCH)
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
            ["git", "apply", str(paths.FNA3D_PATCH)],
            cwd=cache / "lib" / "FNA3D",
            capture_output=True,
        )
        run(
            ["dotnet", "build", FNA_PROJECT, "-c", "Release", "-p:NuGetAudit=false"],
            cwd=cache,
            capture_output=True,
        )
        stamp.write_text(patch)
    for name in FNA_OUTPUTS:
        shutil.copyfile(out / name, dest / name)


def verify_files(root: Path, expected: dict[str, str]) -> list[str]:
    """Returns a problem line per missing, unexpected or mismatching file below root.

    Files build_natives replaced (listed in root's REBUILT_STAMP) are checked against their builds.
    """
    problems = []
    stamp = root / REBUILT_STAMP
    if stamp.is_file():
        expected = {**expected, **json.loads(stamp.read_text())}
    found = {
        p.relative_to(root).as_posix()
        for p in root.rglob("*")
        if p.is_file() and p.name != REBUILT_STAMP
    }
    for rel in sorted(set(expected) - found):
        problems.append(f"missing {rel}")
    for rel in sorted(found - set(expected)):
        problems.append(f"unexpected {rel}")
    for rel in sorted(set(expected) & found):
        if sha256_file(root / rel) != expected[rel]:
            problems.append(f"hash mismatch {rel}")
    return problems


def fetch_natives(pinned: dict, lib: Path, only_stale: bool = False) -> None:
    """Downloads every pinned native-library package into lib/<artifact>/, hash-checked.

    `fnalibs` holds Linux and Windows (x86/x64) libraries, `fnalibs-apple` the macOS ones.
    only_stale: skip the packages lib/ already holds exactly (e.g. on every build, so a
    re-pinned fna.json gets its natives without a new `aether setup`).
    """
    natives = pinned["natives"]
    artifacts = {
        artifact: files
        for artifact, files in natives["artifacts"].items()
        if not only_stale or verify_files(lib / artifact, files)
    }
    if not artifacts:
        return
    log(f"fetching FNA native libraries ({natives['repo']} run {natives['run_id']})…")
    for artifact, files in artifacts.items():
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
            target = lib / artifact
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(tmp, target)


def native_dir() -> str:
    """The fnalibs folder of this Linux machine's architecture."""
    return "libaarch64" if platform.machine() in ("aarch64", "arm64") else "lib64"


def build_natives(pinned: dict, lib: Path) -> None:
    """On Linux, builds SDL3 and FNA3D from the pinned sources with Aether's patches, and puts them
    in lib/fnalibs/ over the downloaded ones (cached by pins and patches).

    Needs cmake and a C compiler; without them the downloaded libraries stay, without GPU frame
    timing. FNA3D comes from FNA's checkout (build_fna, which applies tools/fna3d.patch).
    """
    if not sys.platform.startswith("linux"):
        return
    if shutil.which("cmake") is None or shutil.which("cc") is None:
        log(
            "cmake or a C compiler is missing: keeping the downloaded SDL3 and FNA3D (no GPU frame timing)"
        )
        return
    sdl_pin = pinned["sdl"]
    cache = natives_cache(pinned)
    outputs = {
        "libSDL3.so.0": cache / "sdl-build" / "libSDL3.so.0",
        "libFNA3D.so.0": cache / "fna3d-build" / "libFNA3D.so.0",
    }
    if not all(path.is_file() for path in outputs.values()):
        if cache.exists():
            shutil.rmtree(cache)
        sdl = cache / "sdl"
        cache.mkdir(parents=True)
        log(
            f"building SDL3 ({sdl_pin['commit'][:10]}) and FNA3D with Aether's patches…"
        )
        run(
            [
                "git",
                "clone",
                "--quiet",
                "--filter=blob:none",
                "--no-checkout",
                sdl_pin["repo"],
                str(sdl),
            ],
            capture_output=True,
        )
        run(
            ["git", "checkout", "--quiet", "--detach", sdl_pin["commit"]],
            cwd=sdl,
            capture_output=True,
        )
        run(["git", "apply", str(paths.SDL_PATCH)], cwd=sdl, capture_output=True)
        jobs = str(os.cpu_count() or 2)
        sdl_build = cache / "sdl-build"
        run(
            [
                "cmake",
                "-S",
                str(sdl),
                "-B",
                str(sdl_build),
                "-DCMAKE_BUILD_TYPE=Release",
                "-DSDL_TESTS=OFF",
                "-DSDL_EXAMPLES=OFF",
                "-DSDL_STATIC=OFF",
                "-DSDL_SHARED=ON",
            ],
            capture_output=True,
        )
        run(["cmake", "--build", str(sdl_build), "-j", jobs], capture_output=True)
        fna3d_build = cache / "fna3d-build"
        run(
            [
                "cmake",
                "-S",
                str(paths.FNA_CACHE / pinned["commit"] / "lib" / "FNA3D"),
                "-B",
                str(fna3d_build),
                "-DCMAKE_BUILD_TYPE=Release",
                f"-DSDL3_INCLUDE_DIRS={sdl_include_dirs(cache)}",
                f"-DSDL3_LIBRARIES={sdl_build / 'libSDL3.so'}",
            ],
            capture_output=True,
        )
        run(["cmake", "--build", str(fna3d_build), "-j", jobs], capture_output=True)
    target = lib / "fnalibs" / native_dir()
    target.mkdir(parents=True, exist_ok=True)
    rebuilt = {}
    for name, built in outputs.items():
        # copy follows the builds' version symlinks to the libraries themselves, keeping their mode.
        shutil.copy(built, target / name)
        rebuilt[f"{native_dir()}/{name}"] = sha256_file(target / name)
    (lib / "fnalibs" / REBUILT_STAMP).write_text(json.dumps(rebuilt, indent=2) + "\n")


def natives_cache(pinned: dict) -> Path:
    """Where build_natives builds, named by the pins and patches it builds from."""
    digest = hashlib.sha256()
    for part in (pinned["sdl"]["commit"], pinned["commit"]):
        digest.update(part.encode())
    for patch in (paths.SDL_PATCH, paths.FNA3D_PATCH):
        digest.update(patch.read_bytes())
    return paths.NATIVES_CACHE / digest.hexdigest()[:20]


def sdl_cmake_args(pinned: dict) -> list[str]:
    """CMake arguments that build FNA3D against build_natives' patched SDL (its timestamp
    queries, which tools/fna3d.patch uses), or none if it hasn't been built."""
    cache = natives_cache(pinned)
    library = cache / "sdl-build" / "libSDL3.so"
    if not library.exists():
        return []
    return [
        f"-DSDL3_INCLUDE_DIRS={sdl_include_dirs(cache)}",
        f"-DSDL3_LIBRARIES={library}",
    ]


def sdl_include_dirs(cache: Path) -> str:
    """The patched SDL's headers (source and generated), as SDL3_INCLUDE_DIRS for FNA3D's CMake."""
    build = cache / "sdl-build"
    return ";".join(
        str(p)
        for p in (
            cache / "sdl" / "include",
            build / "include",
            build / "include-config-release",
        )
    )
