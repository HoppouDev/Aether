"""Building the client and running it inside a scratch copy of the game (.aether/run/).

The client is just Terraria.exe, built from src/ against Aether's FNA. Each OS needs it
dropped into a different runtime:

- Linux: the pinned Linux build (the base) with its bundled Mono (MonoKickstart), Content/
  and Steam libraries, but Aether's FNA and its lib64/ natives. The run folder is symlinks
  into the game, except the launcher: it finds lib64/ relative to its real path, so a
  symlinked launcher would load the game's own natives.
- macOS: a copy of the macOS build's Terraria.app, same idea: Contents/MacOS/ (launcher and
  osx/ natives, with fnalibs-apple's FNA libraries) is copied, Contents/Resources/ linked.
  macOS needs that build's own Content/ (its shaders and XACT audio banks differ).
- Windows: no Mono; .NET Framework runs Terraria.exe, built for x86 to match the 32-bit
  steam_api.dll and Steamworks.NET. Content/ comes from the Linux build (the Windows build's
  is XNA's), the natives from fnalibs' x86/, and the Windows-only DLLs and the Windows
  Steamworks.NET (embedded in its Terraria.exe) from the Windows build.
"""

import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from . import paths
from .decompile import ilspy
from .pinning import load_base_json
from .steam import platform_pin
from .util import (
    AetherError,
    link_or_copy,
    log,
    sha256_file,
)
from .util import (
    host_platform as detect_platform,
)

# Replaced in the run folder instead of linked from the game.
CLIENT_FILES = ("Terraria.exe", "FNA.dll", "FNA.dll.config")
LINUX_LAUNCHERS = ("Terraria", "Terraria.bin.x86_64")
MAC_CONTENTS = Path("Terraria.app") / "Contents"
# Files the client needs on Windows that only the Windows build ships: native libraries,
# and the server that Host & Play starts from the run folder.
WINDOWS_RUNTIME = (
    "TerrariaServer.exe",
    "steam_api.dll",
    "nfd.dll",
    "ReLogic.Native.dll",
    "CUESDK_2015.dll",
    "LogitechLedEnginesWrapper.dll",
)
WINDOWS_STEAMWORKS = "Terraria.Libraries.Steamworks.NET.Windows.Steamworks.NET.dll"


def host_platform() -> str:
    platform = detect_platform()
    if platform is None:
        raise AetherError(f"`aether run` doesn't support {sys.platform}.")
    return platform


CONFIGURATIONS = ("Debug", "Release")
# IMAGE_COR20_HEADER flags: the CLR runs the exe as 32-bit with either.
COMIMAGE_FLAGS_32BITREQUIRED = 0x2
COMIMAGE_FLAGS_32BITPREFERRED = 0x20000


def output_exe(configuration: str) -> Path:
    return paths.REPO / "bin" / configuration / "net40" / "Terraria.exe"


def latest_build() -> Path:
    """The most recently built Terraria.exe (Debug or Release)."""
    builds = [exe for c in CONFIGURATIONS if (exe := output_exe(c)).is_file()]
    if not builds:
        raise AetherError(
            "there's no build yet; run `./aether build` (or `./aether run`)."
        )
    return max(builds, key=lambda exe: exe.stat().st_mtime)


def clr_flags(exe: Path) -> int:
    """The CLR header flags of a .NET executable (PE file)."""
    data = exe.read_bytes()
    try:
        (pe,) = struct.unpack_from("<I", data, 0x3C)
        if data[pe : pe + 4] != b"PE\0\0":
            raise ValueError("no PE signature")
        coff = pe + 4
        (sections,) = struct.unpack_from("<H", data, coff + 2)
        (optional_size,) = struct.unpack_from("<H", data, coff + 16)
        optional = coff + 20
        (magic,) = struct.unpack_from("<H", data, optional)
        directories = optional + (96 if magic == 0x10B else 112)  # PE32 / PE32+
        (clr_rva,) = struct.unpack_from("<I", data, directories + 14 * 8)
        table = optional + optional_size
        for i in range(sections):
            size, address, raw_size, raw_offset = struct.unpack_from(
                "<IIII", data, table + i * 40 + 8
            )
            if address <= clr_rva < address + max(size, raw_size):
                (flags,) = struct.unpack_from(
                    "<I", data, raw_offset + clr_rva - address + 16
                )
                return flags
        raise ValueError("no CLR header")
    except (struct.error, ValueError) as e:
        raise AetherError(f"{exe} isn't a .NET executable: {e}") from e


def runs_32_bit(exe: Path) -> bool:
    return bool(
        clr_flags(exe) & (COMIMAGE_FLAGS_32BITREQUIRED | COMIMAGE_FLAGS_32BITPREFERRED)
    )


def check_build(exe: Path, platform: str) -> None:
    """Refuses a build that can't run on platform (Windows needs the 32-bit build)."""
    if platform == "windows" and not runs_32_bit(exe):
        raise AetherError(
            f"{exe} isn't a 32-bit build, so it can't load the 32-bit steam_api.dll and FNA "
            "natives on Windows. Build it with `./aether build` (not plain `dotnet build`)."
        )


def build_command(configuration: str) -> list[str]:
    cmd = [
        "dotnet",
        "build",
        str(paths.REPO / "Terraria.csproj"),
        "-c",
        configuration,
        # No lingering MSBuild nodes or compiler server: they would inherit the
        # captured output pipe and keep it open after the build finishes.
        "-nodeReuse:false",
        "-p:UseSharedCompilation=false",
    ]
    # On Windows, Terraria.csproj itself targets x86 (passing PlatformTarget here wouldn't
    # survive incremental builds, which don't recompile when only a property changes).
    return cmd


def build_client(configuration: str, platform: str) -> Path:
    """Builds Terraria.csproj (and ReLogic) and returns the built Terraria.exe."""
    log(f"building the client ({configuration}, {platform})…")
    result = subprocess.run(
        build_command(configuration),
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        errors = [line for line in result.stdout.splitlines() if " error " in line]
        raise AetherError(
            "build failed:\n"
            + "\n".join(dict.fromkeys(errors) or [result.stdout[-2000:]])
        )
    exe = output_exe(configuration)
    if not exe.is_file():
        raise AetherError(f"the build didn't produce {exe}")
    return exe


def require_fna(natives: str) -> Path:
    """lib/<natives>/ (one of FNA's native folders), as fetched by `aether setup`."""
    path = paths.LIB / natives
    if not (paths.LIB / "FNA.dll").is_file() or not path.is_dir():
        raise AetherError("Aether's FNA isn't in lib/ yet. Run `./aether setup` first.")
    return path


def warn_if_unpinned(platform_dir: Path, platform: str) -> str | None:
    """Warns unless platform_dir is the pinned build; returns its exe's sha256 (if any)."""
    pin = platform_pin(load_base_json(), platform)
    exe = platform_dir / pin["exe"]
    digest = sha256_file(exe) if exe.is_file() else None
    if digest != pin["exe_sha256"]:
        download = "./aether download" + (
            "" if platform == "linux" else f" --platform {platform}"
        )
        log(
            f"warning: {platform_dir} isn't the pinned {platform} build; its files may not "
            f"match the client. `{download}` gets the pinned one."
        )
    return digest


def copy_client(exe: Path, dest: Path) -> None:
    """The built client (+ its .pdb) and Aether's FNA into dest."""
    shutil.copy2(exe, dest / "Terraria.exe")
    pdb = exe.with_suffix(".pdb")
    if pdb.is_file():
        shutil.copy2(pdb, dest / pdb.name)
    for name in ("FNA.dll", "FNA.dll.config"):
        shutil.copy2(paths.LIB / name, dest / name)


def replace_natives(natives: Path, dest: Path) -> None:
    """Copies every library in natives into dest, replacing what's there."""
    dest.mkdir(exist_ok=True)
    for lib in natives.iterdir():
        target = dest / lib.name
        if target.exists() or target.is_symlink():
            target.unlink()
        shutil.copy2(lib, target)


def fresh_dir(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True)


def prepare_linux(game_dir: Path, exe: Path, run_dir: Path) -> None:
    for launcher in LINUX_LAUNCHERS:
        if not (game_dir / launcher).is_file():
            raise AetherError(f"{game_dir} has no {launcher}; is it the Linux build?")
    natives = require_fna("fnalibs/lib64")
    warn_if_unpinned(game_dir, "linux")
    fresh_dir(run_dir)
    for entry in game_dir.iterdir():
        if entry.name not in (*CLIENT_FILES, *LINUX_LAUNCHERS, "lib64"):
            link_or_copy(entry, run_dir / entry.name)
    for launcher in LINUX_LAUNCHERS:
        shutil.copy2(game_dir / launcher, run_dir / launcher)
    # lib64/: the game's natives (Steam, file dialogs), with FNA's replaced by Aether's.
    lib64 = run_dir / "lib64"
    lib64.mkdir()
    for lib in (game_dir / "lib64").iterdir() if (game_dir / "lib64").is_dir() else []:
        link_or_copy(lib, lib64 / lib.name)
    replace_natives(natives, lib64)
    copy_client(exe, run_dir)


def prepare_macos(platform_dir: Path, exe: Path, run_dir: Path) -> None:
    contents = platform_dir / MAC_CONTENTS
    if not (contents / "MacOS" / "Terraria.bin.osx").is_file():
        raise AetherError(
            f"{platform_dir} has no {MAC_CONTENTS}; is it the macOS build?"
        )
    natives = require_fna("fnalibs-apple/osx")
    warn_if_unpinned(platform_dir, "macos")
    fresh_dir(run_dir)
    out = run_dir / MAC_CONTENTS
    out.mkdir(parents=True)
    for entry in contents.iterdir():
        if entry.name == "MacOS":
            # Launcher, kickstart and osx/ natives as real files, FNA's swapped in.
            shutil.copytree(entry, out / "MacOS")
            replace_natives(natives, out / "MacOS" / "osx")
        elif entry.name == "Resources":
            (out / "Resources").mkdir()
            for item in entry.iterdir():
                if item.name not in CLIENT_FILES:
                    link_or_copy(item, out / "Resources" / item.name)
        elif entry.name == "_CodeSignature":
            continue  # the copy isn't the signed bundle anymore
        elif entry.is_file():
            shutil.copy2(entry, out / entry.name)
        else:
            link_or_copy(entry, out / entry.name)
    copy_client(exe, out / "Resources")


def windows_steamworks(platform_dir: Path, exe_sha256: str | None = None) -> Path:
    """The Windows build's Steamworks.NET.dll (embedded in its Terraria.exe), cached in lib/."""
    exe = platform_dir / "Terraria.exe"
    digest = exe_sha256 or sha256_file(exe)
    cache = paths.LIB / "windows" / digest[:16] / "Steamworks.NET.dll"
    if not cache.is_file():
        with tempfile.TemporaryDirectory(prefix="aether-steamworks-") as tmp:
            ilspy(
                "--resource",
                WINDOWS_STEAMWORKS,
                "-o",
                tmp,
                str(exe),
                capture_output=True,
            )
            extracted = Path(tmp) / WINDOWS_STEAMWORKS
            if not extracted.is_file():
                raise AetherError(f"{exe} has no embedded {WINDOWS_STEAMWORKS}.")
            cache.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(extracted, cache)
    return cache


def prepare_windows(
    game_dir: Path, platform_dir: Path, exe: Path, run_dir: Path
) -> None:
    if (
        not (game_dir / "Content").is_dir()
        or not (game_dir / "Terraria.bin.x86_64").is_file()
    ):
        raise AetherError(
            f"{game_dir} isn't the Linux build, whose Content/ the client needs on Windows; "
            "run `./aether download` (or pass --game-dir)."
        )
    if not (platform_dir / "steam_api.dll").is_file():
        raise AetherError(
            f"{platform_dir} has no steam_api.dll; is it the Windows build? "
            "`./aether download --platform windows` gets the pinned one (or pass --platform-dir)."
        )
    natives = require_fna("fnalibs/x86")
    exe_sha256 = warn_if_unpinned(platform_dir, "windows")
    fresh_dir(run_dir)
    link_or_copy(game_dir / "Content", run_dir / "Content")
    shutil.copy2(game_dir / "steam_appid.txt", run_dir / "steam_appid.txt")
    for name in WINDOWS_RUNTIME:
        if (platform_dir / name).is_file():
            shutil.copy2(platform_dir / name, run_dir / name)
    # Next to the exe, so the CLR loads it instead of the Linux one embedded in the client.
    shutil.copy2(
        windows_steamworks(platform_dir, exe_sha256), run_dir / "Steamworks.NET.dll"
    )
    replace_natives(natives, run_dir)
    copy_client(exe, run_dir)


def prepare_run_dir(
    platform: str, game_dir: Path, platform_dir: Path, exe: Path, run_dir: Path
) -> None:
    """(Re)creates run_dir for platform. game_dir is the Linux base; platform_dir the
    macOS/Windows build (unused on Linux)."""
    if platform == "linux":
        prepare_linux(game_dir, exe, run_dir)
    elif platform == "macos":
        prepare_macos(platform_dir, exe, run_dir)
    elif platform == "windows":
        prepare_windows(game_dir, platform_dir, exe, run_dir)
    else:
        raise AetherError(f"unknown platform {platform}")


def launcher(platform: str, run_dir: Path) -> Path:
    if platform == "macos":
        return run_dir / MAC_CONTENTS / "MacOS" / "Terraria"
    if platform == "windows":
        return run_dir / "Terraria.exe"
    return run_dir / "Terraria"


def launch_command(
    platform: str, run_dir: Path, save_dir: Path | None, args: list[str]
) -> list[str]:
    cmd = [str(launcher(platform, run_dir))]
    if save_dir is not None:
        cmd += ["-savedirectory", str(save_dir)]
    return cmd + args


def launch(platform: str, run_dir: Path, save_dir: Path | None, args: list[str]) -> int:
    """Runs the client in the foreground and returns its exit code."""
    if save_dir is not None:
        save_dir.mkdir(parents=True, exist_ok=True)
    cmd = launch_command(platform, run_dir, save_dir, args)
    log("starting the client" + (f" (saves in {save_dir})" if save_dir else "") + "…")
    return subprocess.run(cmd, cwd=Path(cmd[0]).parent, check=False).returncode
