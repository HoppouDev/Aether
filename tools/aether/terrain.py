"""Build and stage the optional Linux/Vulkan terrain extension from the pinned FNA tree."""

import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
from .fna import (
    FNA_OUTPUT_DIR,
    FNA_OUTPUTS,
    FNA_PATCH_STAMP,
    FNA_PROJECT,
    load_fna_json,
)
from .util import AetherError, log

SOURCES = paths.REPO / "tools" / "terrain"
TOOLS = ("cmake", "ninja", "patch", "glslangValidator", "spirv-val")


def build_backend() -> Path:
    pin = load_fna_json()["commit"]
    source = paths.FNA_CACHE / pin
    if not (source / "lib" / "FNA3D" / "CMakeLists.txt").is_file():
        raise AetherError("Run ./aether setup to obtain the pinned FNA source.")
    digest = hashlib.sha256(pin.encode())
    # The FNA build it bundles (a .NET Framework one before the move to .NET 10), and
    # the version of Aether's patch to FNA that the checkout it copies has applied.
    digest.update(FNA_PROJECT.encode())
    stamp = source / FNA_PATCH_STAMP
    if stamp.is_file():
        digest.update(stamp.read_bytes())
    for file in sorted(SOURCES.rglob("*")):
        if file.is_file():
            digest.update(file.relative_to(SOURCES).as_posix().encode())
            digest.update(file.read_bytes())
    cache = paths.FNA_CACHE / ("terrain-" + digest.hexdigest()[:20])
    if (cache / "AetherTerrain" / "manifest.json").is_file():
        return cache

    def run(*args, cwd=None):
        subprocess.run(args, cwd=cwd, check=True)

    log("building Aether's Vulkan terrain extension")
    with tempfile.TemporaryDirectory(
        prefix="terrain-", dir=paths.FNA_CACHE
    ) as temporary:
        work = Path(temporary)
        tree = work / "fna"
        shutil.copytree(
            source,
            tree,
            ignore=shutil.ignore_patterns(".git", "bin", "obj", "obj_framework"),
        )
        native = tree / "lib" / "FNA3D"
        for patch, directory in (("FNA.patch", tree), ("FNA3D.patch", native)):
            for line in (SOURCES / patch).read_text().splitlines():
                if line.startswith("--- a/"):
                    path = directory / line[6:]
                    path.write_text(path.read_text())
            run(
                "patch",
                "--batch",
                "--fuzz=0",
                "-p1",
                "-i",
                str(SOURCES / patch),
                cwd=directory,
            )
        for include in SOURCES.glob("*.inl"):
            shutil.copy2(include, native / "src")
        run(
            "dotnet",
            "build",
            FNA_PROJECT,
            "-c",
            "Release",
            "-p:NuGetAudit=false",
            cwd=tree,
        )
        run(
            "cmake",
            "-S",
            str(native),
            "-B",
            str(work / "native"),
            "-G",
            "Ninja",
            "-DBUILD_SDL3=ON",
            "-DCMAKE_BUILD_TYPE=Release",
            "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
        )
        run("cmake", "--build", str(work / "native"), "--parallel", "2")
        output = work / "output"
        shaders = output / "AetherTerrain"
        shaders.mkdir(parents=True)
        for name in FNA_OUTPUTS:
            shutil.copy2(tree / FNA_OUTPUT_DIR / name, output / name)
        shutil.copy2(work / "native" / "libFNA3D.so.0", output / "libFNA3D.so.0")
        for shader in sorted((SOURCES / "shaders").iterdir()):
            if shader.suffix not in {".vert", ".frag", ".comp"}:
                continue
            binary = shaders / (shader.name + ".spv")
            run(
                "glslangValidator",
                "-V",
                "--target-env",
                "vulkan1.0",
                str(shader),
                "-o",
                str(binary),
            )
            run("spirv-val", "--target-env", "vulkan1.0", str(binary))
        (shaders / "manifest.json").write_text(
            json.dumps(
                {
                    "abi": 1,
                    "fna_commit": pin,
                    "source_hash": digest.hexdigest(),
                    "terrain_record_version": 3,
                    "terrain_compute_record_version": 1,
                },
                indent=2,
            )
            + "\n"
        )
        output.rename(cache)
    return cache


def stage_linux_backend(destination: Path) -> None:
    missing = [tool for tool in TOOLS if shutil.which(tool) is None]
    if missing:
        log("Vulkan terrain extension unavailable; missing " + ", ".join(missing))
        return
    try:
        backend = build_backend()
    except (AetherError, OSError, subprocess.CalledProcessError) as error:
        log(f"Vulkan terrain extension unavailable: {error}")
        return
    for name in FNA_OUTPUTS:
        shutil.copy2(backend / name, destination / name)
    # Next to the client, with the other native libraries.
    shutil.copy2(backend / "libFNA3D.so.0", destination / "libFNA3D.so.0")
    shutil.copytree(
        backend / "AetherTerrain", destination / "AetherTerrain", dirs_exist_ok=True
    )
