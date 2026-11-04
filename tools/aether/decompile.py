"""Building .base/ and lib/ from the user's Terraria.exe (the decompile recipe).

Any change to the output of this module changes .base/: bump `recipe` in tools/base.json.
"""

import concurrent.futures
import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
from .fna import build_fna, fetch_natives, load_fna_json
from .pinning import load_base_json, write_stamp
from .tree import tree_files
from .util import AetherError, link_or_copy, log, run, sha256_file

# Embedded library resource -> file name used in lib/ (and referenced by Terraria.csproj).
LIBRARY_RESOURCES = {
    "Terraria.Libraries.ReLogic.ReLogic.dll": "ReLogic.dll",
    "Terraria.Libraries.DotNetZip.Ionic.Zip.CF.dll": "Ionic.Zip.CF.dll",
    "Terraria.Libraries.JSON.NET.Newtonsoft.Json.dll": "Newtonsoft.Json.dll",
    "Terraria.Libraries.CsvHelper.CsvHelper.dll": "CsvHelper.dll",
    "Terraria.Libraries.NVorbis.NVorbis.dll": "NVorbis.dll",
    "Terraria.Libraries.NVorbis.System.ValueTuple.dll": "System.ValueTuple.dll",
    "Terraria.Libraries.MP3Sharp.MP3Sharp.dll": "MP3Sharp.dll",
    "Terraria.Libraries.Steamworks.NET.Linux.Steamworks.NET.dll": "Steamworks.NET.dll",
    "Terraria.Libraries.RailSDK.Linux.RailSDK.Net.dll": "RailSDK.Net.dll",
    "Terraria.Libraries.SteelSeries.SteelSeriesEngineWrapper.dll": "SteelSeriesEngineWrapper.dll",
}


# Libraries from lib/ that are also decompiled into .base/<folder>/, so patches can change
# them too. Terraria.csproj builds them from src/ instead of using the extracted DLLs.
PATCHABLE_LIBRARIES = {"ReLogic.dll": "ReLogic"}


def ilspy(*cmd_args: str, **kwargs) -> subprocess.CompletedProcess:
    """Runs the ilspycmd version pinned in dotnet-tools.json."""
    return run(
        ["dotnet", "ilspycmd", "--disable-updatecheck", *cmd_args],
        cwd=paths.REPO,
        **kwargs,
    )


def exe_version(exe: Path) -> str:
    """The game version of a Terraria.exe: its assembly version (e.g. "1.4.5.8")."""
    out = ilspy(
        "--dump-table", "Assembly", "--json", str(exe), capture_output=True
    ).stdout
    try:
        return json.loads(out)["rows"][0]["Version"]
    except (ValueError, KeyError, IndexError) as e:
        raise AetherError(f"couldn't read the version of {exe}") from e


def reference_assemblies_dir() -> Path:
    """The .NET Framework 4.0 reference assemblies from the NuGet cache (restored if missing)."""

    def find() -> Path | None:
        out = run(
            ["dotnet", "nuget", "locals", "global-packages", "--list"],
            capture_output=True,
        ).stdout
        # Match the line itself: on a fresh machine dotnet may print a first-run banner first.
        match = re.search(r"^global-packages:\s*(.+?)\s*$", out, re.MULTILINE)
        if not match:
            raise AetherError(
                f"unexpected `dotnet nuget locals` output:\n{out.strip()}"
            )
        packages = Path(match.group(1))
        candidates = sorted(
            packages.glob(
                "microsoft.netframework.referenceassemblies.net40/*/build/.NETFramework/v4.0"
            )
        )
        return candidates[-1] if candidates else None

    found = find()
    if found is None:
        log("restoring .NET Framework 4.0 reference assemblies…")
        run(
            ["dotnet", "restore", str(paths.REPO / "Terraria.csproj")],
            capture_output=True,
        )
        found = find()
    if found is None:
        raise AetherError(
            "couldn't find the .NET Framework 4.0 reference assemblies in the NuGet cache."
        )
    return found


def normalize_line_endings(data: bytes) -> bytes:
    """CRLF -> LF, like `sed 's/\\r$//'` (also strips a CR right before end of file)."""
    return re.sub(rb"\r(?=\n|\Z)", b"", data)


def expand_leading_tabs(data: bytes, tabsize: int = 4) -> bytes:
    """Equivalent of `expand -i -t 4`: converts tabs in each line's leading whitespace only."""
    out = []
    for line in data.split(b"\n"):
        i = 0
        col = 0
        while i < len(line) and line[i] in (0x20, 0x09):
            col = col + tabsize - col % tabsize if line[i] == 0x09 else col + 1
            i += 1
        out.append(b" " * col + line[i:] if b"\t" in line[:i] else line)
    return b"\n".join(out)


def extract_resources(exe: Path, out_dir: Path) -> list[str]:
    """Extracts every manifest resource of exe into out_dir. Returns the resource names."""
    names = [
        n
        for n in ilspy(
            "--list-resources", str(exe), capture_output=True
        ).stdout.splitlines()
        if n.strip()
    ]
    if not names:
        raise AetherError("no embedded resources found in Terraria.exe")

    def extract(name: str) -> None:
        ilspy("--resource", name, "-o", str(out_dir), str(exe), capture_output=True)
        if not (out_dir / name).is_file():
            raise AetherError(f"failed to extract resource {name}")

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        list(pool.map(extract, names))
    missing = set(LIBRARY_RESOURCES) - set(names)
    if missing:
        raise AetherError(f"expected library resources not found: {sorted(missing)}")
    return names


def library_source_path(rel: Path, root_namespace: str) -> Path:
    """Where a file of a decompiled library goes below its .base/<folder>/.

    ILSpy nests the library's namespaces under a folder named after its root namespace
    (ReLogic/Content/X.cs); that level is dropped, so the folder itself is the root
    namespace. Other files (Properties/AssemblyInfo.cs) keep their path.
    """
    if len(rel.parts) > 1 and rel.parts[0] == root_namespace:
        return Path(*rel.parts[1:])
    return rel


def decompile_project(
    assembly: Path, work_dir: Path, dest: Path, root_namespace: str | None = None
) -> int:
    """Decompiles assembly as a project and writes its normalized .cs files into dest.

    Returns the number of .cs files written.
    """
    work_dir.mkdir(parents=True)
    ilspy(
        "--nested-directories",
        "-p",
        "-o",
        str(work_dir),
        str(assembly),
        capture_output=True,
    )
    cs_files = list(work_dir.rglob("*.cs"))
    for path in cs_files:
        rel = path.relative_to(work_dir)
        if root_namespace:
            rel = library_source_path(rel, root_namespace)
        out = dest / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(expand_leading_tabs(path.read_bytes()))
    return len(cs_files)


def check_exe(exe: Path, pinned: dict, allow_mismatch: bool) -> str:
    """Returns the exe's sha256, failing if it isn't the pinned build (unless allowed)."""
    exe_sha = sha256_file(exe)
    if exe_sha != pinned["exe_sha256"]:
        message = (
            f"Terraria.exe ({exe_sha[:12]}…) isn't the pinned {pinned['game_version']} build "
            f"({pinned['exe_sha256'][:12]}…); patches may not apply."
        )
        if not allow_mismatch:
            raise AetherError(
                message + " Pass --allow-version-mismatch to continue anyway."
            )
        log("warning: " + message)
    return exe_sha


def build_base(game_dir: Path, allow_mismatch: bool) -> None:
    """Regenerates .base/ and lib/ from game_dir. Both are replaced only if everything succeeds."""
    pinned = load_base_json()
    exe = game_dir / "Terraria.exe"
    fna = game_dir / "FNA.dll"
    for required in (exe, fna, game_dir / "Terraria.bin.x86_64"):
        if not required.is_file():
            raise AetherError(
                f"{required} not found: {game_dir} isn't the Linux build of Terraria (the "
                "base). Run `./aether download` to get it, or pass --game-dir / set $TERRARIA_DIR."
            )
    exe_sha = check_exe(exe, pinned, allow_mismatch)
    ref_dir = reference_assemblies_dir()

    with tempfile.TemporaryDirectory(prefix="aether-setup-") as tmp_name:
        tmp = Path(tmp_name)
        resources, refs, decomp = tmp / "resources", tmp / "refs", tmp / "decomp"
        new_base, new_lib = tmp / "base", tmp / "lib"
        for d in (resources, refs, new_base / "Resources", new_lib):
            d.mkdir(parents=True)

        log("extracting embedded resources…")
        for name in extract_resources(exe, resources):
            data = (resources / name).read_bytes()
            if name in LIBRARY_RESOURCES:
                (new_lib / LIBRARY_RESOURCES[name]).write_bytes(data)
            else:
                (new_base / "Resources" / name).write_bytes(
                    normalize_line_endings(data)
                )
        # The client builds and runs on Aether's pinned FNA, not the game's older copy.
        fna_pin = load_fna_json()
        build_fna(fna_pin, new_lib)
        fetch_natives(fna_pin, new_lib)

        # Decompile against the net40 reference assemblies (not the game's Mono mscorlib,
        # which makes ILSpy emit MathF.PI), the extracted libraries, and the game's own
        # FNA.dll: the one Terraria.exe was compiled against, so .base/ doesn't depend on
        # Aether's FNA version.
        for ref in ref_dir.glob("*.dll"):
            link_or_copy(ref, refs / ref.name)
        for lib in [*new_lib.glob("*.dll"), fna]:
            target = refs / lib.name
            if target.exists() or target.is_symlink():
                target.unlink()
            link_or_copy(lib, target)
        link_or_copy(exe, refs / "Terraria.exe")

        log(f"decompiling with ilspycmd {pinned['ilspycmd']} (takes ~20s)…")
        count = decompile_project(refs / "Terraria.exe", decomp / "Terraria", new_base)
        if count < 1000:
            raise AetherError(f"decompilation produced only {count} .cs files")
        for dll, folder in PATCHABLE_LIBRARIES.items():
            if not decompile_project(
                refs / dll, decomp / folder, new_base / folder, folder
            ):
                raise AetherError(f"decompiling {dll} produced no .cs files")

        write_stamp(new_base, exe_sha, pinned)
        for final, built in ((paths.BASE, new_base), (paths.LIB, new_lib)):
            if final.exists():
                shutil.rmtree(final)
            shutil.move(str(built), str(final))
    log(
        f".base/: {len(tree_files(paths.BASE))} files; lib/: {len(list(paths.LIB.glob('*.dll')))} DLLs."
    )
