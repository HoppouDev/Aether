"""Building .base/ and lib/ from the user's Terraria.exe (the decompile recipe).

Any change to the output of this module changes .base/: bump `recipe` in tools/base.json.
"""

import concurrent.futures
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import paths
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


def ilspy(*cmd_args: str, **kwargs) -> subprocess.CompletedProcess:
    """Runs the ilspycmd version pinned in dotnet-tools.json."""
    return run(
        ["dotnet", "ilspycmd", "--disable-updatecheck", *cmd_args],
        cwd=paths.REPO,
        **kwargs,
    )


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
    for required in (exe, fna):
        if not required.is_file():
            raise AetherError(
                f"{required} not found. Pass --game-dir or set $TERRARIA_DIR."
            )
    exe_sha = check_exe(exe, pinned, allow_mismatch)
    ref_dir = reference_assemblies_dir()

    with tempfile.TemporaryDirectory(prefix="aether-setup-") as tmp_name:
        tmp = Path(tmp_name)
        resources, refs, decomp = tmp / "resources", tmp / "refs", tmp / "decomp"
        new_base, new_lib = tmp / "base", tmp / "lib"
        for d in (resources, refs, decomp, new_base / "Resources", new_lib):
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
        shutil.copyfile(fna, new_lib / "FNA.dll")

        # Decompile against the net40 reference assemblies (not the game's Mono mscorlib,
        # which makes ILSpy emit MathF.PI) plus the extracted libraries.
        for ref in ref_dir.glob("*.dll"):
            link_or_copy(ref, refs / ref.name)
        for lib in new_lib.glob("*.dll"):
            target = refs / lib.name
            if target.exists() or target.is_symlink():
                target.unlink()
            link_or_copy(lib, target)
        link_or_copy(exe, refs / "Terraria.exe")

        log(f"decompiling with ilspycmd {pinned['ilspycmd']} (takes ~20s)…")
        ilspy(
            "--nested-directories",
            "-p",
            "-o",
            str(decomp),
            str(refs / "Terraria.exe"),
            capture_output=True,
        )

        cs_files = list(decomp.rglob("*.cs"))
        if len(cs_files) < 1000:
            raise AetherError(f"decompilation produced only {len(cs_files)} .cs files")
        for path in cs_files:
            dst = new_base / path.relative_to(decomp)
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(expand_leading_tabs(path.read_bytes()))

        write_stamp(new_base, exe_sha, pinned)
        for final, built in ((paths.BASE, new_base), (paths.LIB, new_lib)):
            if final.exists():
                shutil.rmtree(final)
            shutil.move(str(built), str(final))
    log(
        f".base/: {len(tree_files(paths.BASE))} files; lib/: {len(list(paths.LIB.glob('*.dll')))} DLLs."
    )
