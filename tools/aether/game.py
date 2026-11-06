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
- Windows: no Mono; .NET Framework runs the same AnyCPU Terraria.exe as a 64-bit process, with
  Content/ from the Linux build (the Windows build's is XNA's), fnalibs' x64 natives and D3D12
  Agility SDK, and the official 64-bit Steamworks.NET + steam_api64.dll. The Windows build only
  adds the vanilla 32-bit TerrariaServer.exe (+ its steam_api.dll) for Host & Play.

`aether run` builds that layout in .aether/run/ with symlinks; `aether build --target` copies
everything into a self-contained dist/<target>/ (which therefore contains game files).
"""

import os
import shutil
import struct
import subprocess
import sys
from pathlib import Path

from . import paths
from .pinning import load_base_json
from .steam import platform_pin
from .terrain import stage_linux_backend
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
# From the Windows build: the vanilla server Host & Play starts from the client's folder. It's a
# 32-bit process, so it loads its own 32-bit steam_api.dll (the client uses steam_api64.dll).
WINDOWS_SERVER = ("TerrariaServer.exe", "steam_api.dll")
WINDOWS_STEAMWORKS = ("Steamworks.NET.dll", "steam_api64.dll")
# Build targets for `aether build --target`, and the OS layout each one uses.
TARGETS = {"lin64": "linux", "win64": "windows", "mac": "macos"}
TARGET_ALIASES = {"mac64": "mac", "macarm": "mac"}  # one universal macOS bundle


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
    """Refuses a build that can't run on platform (Windows runs it 64-bit, with x64 natives)."""
    if platform == "windows" and runs_32_bit(exe):
        raise AetherError(
            f"{exe} is a 32-bit build, but the client runs as a 64-bit process on Windows (x64 "
            "FNA natives and steam_api64.dll). Rebuild it with `./aether build`."
        )


def resolve_target(target: str) -> list[str]:
    """Target names for `--target` (an alias, a target, or "all")."""
    if target == "all":
        return list(TARGETS)
    target = TARGET_ALIASES.get(target, target)
    if target not in TARGETS:
        names = ", ".join([*TARGETS, *TARGET_ALIASES, "all"])
        raise AetherError(f"unknown target {target!r}; use one of: {names}.")
    return [target]


def build_command(configuration: str) -> list[str]:
    return [
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


def place(src: Path, dst: Path, copy: bool) -> None:
    """Links (run folders) or copies (self-contained dist folders) src to dst."""
    if not copy:
        link_or_copy(src, dst)
    elif src.is_dir():
        shutil.copytree(src, dst)
    else:
        shutil.copy2(src, dst)


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


def prepare_linux(game_dir: Path, exe: Path, run_dir: Path, copy: bool = False) -> None:
    for launcher in LINUX_LAUNCHERS:
        if not (game_dir / launcher).is_file():
            raise AetherError(f"{game_dir} has no {launcher}; is it the Linux build?")
    natives = require_fna("fnalibs/lib64")
    warn_if_unpinned(game_dir, "linux")
    fresh_dir(run_dir)
    for entry in game_dir.iterdir():
        if entry.name not in (*CLIENT_FILES, *LINUX_LAUNCHERS, "lib64"):
            place(entry, run_dir / entry.name, copy)
    for launcher in LINUX_LAUNCHERS:
        shutil.copy2(game_dir / launcher, run_dir / launcher)
    # lib64/: the game's natives (Steam, file dialogs), with FNA's replaced by Aether's.
    lib64 = run_dir / "lib64"
    lib64.mkdir()
    for lib in (game_dir / "lib64").iterdir() if (game_dir / "lib64").is_dir() else []:
        place(lib, lib64 / lib.name, copy)
    replace_natives(natives, lib64)
    copy_client(exe, run_dir)
    stage_linux_backend(run_dir)


def prepare_macos(
    platform_dir: Path, exe: Path, run_dir: Path, copy: bool = False
) -> None:
    contents = platform_dir / MAC_CONTENTS
    if not (contents / "MacOS" / "Terraria.bin.osx").is_file():
        raise AetherError(
            f"{platform_dir} has no {MAC_CONTENTS}; is it the macOS build? "
            "`./aether download --platform macos` gets it (or pass --platform-dir)."
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
                    place(item, out / "Resources" / item.name, copy)
        elif entry.name == "_CodeSignature":
            continue  # the copy isn't the signed bundle anymore
        elif entry.is_file():
            shutil.copy2(entry, out / entry.name)
        else:
            place(entry, out / entry.name, copy)
    copy_client(exe, out / "Resources")


def prepare_windows(
    game_dir: Path,
    platform_dir: Path | None,
    exe: Path,
    run_dir: Path,
    copy: bool = False,
) -> None:
    if (
        not (game_dir / "Content").is_dir()
        or not (game_dir / "Terraria.bin.x86_64").is_file()
    ):
        raise AetherError(
            f"{game_dir} isn't the Linux build, whose Content/ the client needs on Windows; "
            "run `./aether download` (or pass --game-dir)."
        )
    natives = require_fna("fnalibs/x64")
    steamworks = paths.LIB / "steamworks" / "Windows-x64"
    if not all((steamworks / name).is_file() for name in WINDOWS_STEAMWORKS):
        raise AetherError(
            "lib/ has no 64-bit Steamworks.NET yet. Run `./aether setup` first."
        )
    fresh_dir(run_dir)
    place(game_dir / "Content", run_dir / "Content", copy)
    shutil.copy2(game_dir / "steam_appid.txt", run_dir / "steam_appid.txt")
    replace_natives(natives, run_dir)
    # FNA3D's Direct3D 12 backend needs the Agility SDK in a D3D12/ folder next to the exe.
    shutil.copytree(paths.LIB / "fnalibs" / "D3D12", run_dir / "D3D12")
    # Next to the exe, so the CLR loads this 64-bit Steamworks.NET instead of the Linux one
    # embedded in the client.
    for name in WINDOWS_STEAMWORKS:
        shutil.copy2(steamworks / name, run_dir / name)
    if platform_dir is not None and all(
        (platform_dir / name).is_file() for name in WINDOWS_SERVER
    ):
        warn_if_unpinned(platform_dir, "windows")
        for name in WINDOWS_SERVER:
            shutil.copy2(platform_dir / name, run_dir / name)
    else:
        log(
            "warning: no Windows build (`./aether download --platform windows`), so there's no "
            "TerrariaServer.exe and Host & Play won't work; everything else does."
        )
    copy_client(exe, run_dir)


def prepare_run_dir(
    platform: str,
    game_dir: Path,
    platform_dir: Path | None,
    exe: Path,
    run_dir: Path,
    copy: bool = False,
) -> None:
    """(Re)creates run_dir for platform. game_dir is the Linux base; platform_dir the
    macOS/Windows build (unused on Linux, optional on Windows). copy makes it self-contained."""
    if platform == "linux":
        prepare_linux(game_dir, exe, run_dir, copy)
    elif platform == "macos":
        if platform_dir is None:
            raise AetherError(
                "the macOS layout needs the macOS build (--platform-dir)."
            )
        prepare_macos(platform_dir, exe, run_dir, copy)
    elif platform == "windows":
        prepare_windows(game_dir, platform_dir, exe, run_dir, copy)
    else:
        raise AetherError(f"unknown platform {platform}")


def package(target: str, exe: Path, game_dir: Path, platform_dir: Path | None) -> Path:
    """Builds the self-contained dist/<target>/ for target (lin64, win64 or mac)."""
    dest = paths.DIST / target
    log(
        f"packaging {target} into {dest.relative_to(paths.REPO)}/ (copies the game files)…"
    )
    prepare_run_dir(TARGETS[target], game_dir, platform_dir, exe, dest, copy=True)
    return dest


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


def launch(
    platform: str,
    run_dir: Path,
    save_dir: Path | None,
    args: list[str],
    env: dict[str, str] | None = None,
) -> int:
    """Runs the client in the foreground (env: extra environment) and returns its exit code."""
    if save_dir is not None:
        save_dir.mkdir(parents=True, exist_ok=True)
    cmd = launch_command(platform, run_dir, save_dir, args)
    log("starting the client" + (f" (saves in {save_dir})" if save_dir else "") + "…")
    return subprocess.run(
        cmd,
        cwd=Path(cmd[0]).parent,
        check=False,
        env={**os.environ, **env} if env else None,
    ).returncode
