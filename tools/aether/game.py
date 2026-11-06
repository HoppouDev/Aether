"""Building the client and running it inside a scratch copy of the game (.aether/run/).

The client is a .NET 10 app (Terraria.dll with an apphost launcher), built from src/ against
Aether's FNA (its .NET build). The game's own builds run on Mono or .NET Framework; the client
replaces their runtime and keeps their content and libraries:

- Linux: the client and every native library side by side in the run folder (Aether's FNA
  natives over the game's Steam and file-dialog libraries from lib64/), with the Linux build's
  Content/ and other files linked in. The vanilla server that Host & Play starts is a separate
  Mono program needing its own (older) FNA, so it gets a copy of the game's layout in server/;
  its launcher is a real copy there, because MonoKickstart finds lib64/ relative to it.
- macOS: a copy of the macOS build's Terraria.app with the client in Contents/Client/ (with its
  natives and a link to the bundle's Content/: macOS needs that build's own shaders and audio
  banks), started by a Contents/MacOS/Terraria script; the vanilla server keeps the bundle's
  MacOS/ and Resources/, which is where the game starts it from (../MacOS/TerrariaServer).
- Windows: the client with Content/ from the Linux build (the Windows build's is XNA's),
  fnalibs' x64 natives and D3D12 Agility SDK, and the official 64-bit Steamworks.NET +
  steam_api64.dll. The Windows build only adds the vanilla 32-bit TerrariaServer.exe (+ its
  steam_api.dll) for Host & Play.

`aether run` builds the client framework-dependent (it runs on the installed .NET 10) and lays it
out in .aether/run/ with symlinks; `aether build --target` publishes it self-contained for the
target's runtime and copies everything into dist/<target>/ (which therefore contains game files).
"""

import filecmp
import os
import shutil
import subprocess
import sys
from pathlib import Path

from . import paths
from .fna import FNA_OUTPUT_DIR, load_fna_json
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

TARGET_FRAMEWORK = "net10.0"
CLIENT_DLL = "Terraria.dll"
# What the game folders hold that the client replaces or doesn't use: MonoKickstart's launchers,
# the game's exe and FNA, Mono's configs, and (any *.dll) Mono's class libraries.
LINUX_LAUNCHERS = ("Terraria", "Terraria.bin.x86_64")
MONO_FILES = (
    *LINUX_LAUNCHERS,
    "Terraria.exe",
    "FNA.dll.config",
    "monoconfig",
    "monomachineconfig",
)
LINUX_SERVER = ("TerrariaServer", "TerrariaServer.bin.x86_64", "TerrariaServer.exe")
SERVER_DIR = "server"
MAC_CONTENTS = Path("Terraria.app") / "Contents"
MAC_CLIENT = MAC_CONTENTS / "Client"
# From the Windows build: the vanilla server Host & Play starts from the client's folder. It's a
# 32-bit process, so it loads its own 32-bit steam_api.dll (the client uses steam_api64.dll).
WINDOWS_SERVER = ("TerrariaServer.exe", "steam_api.dll")
WINDOWS_STEAMWORKS = ("Steamworks.NET.dll", "steam_api64.dll")
# Build targets for `aether build --target`: the OS layout and .NET runtime each one uses.
TARGETS = {"lin64": "linux", "win64": "windows", "mac64": "macos", "macarm": "macos"}
RUNTIMES = {
    "lin64": "linux-x64",
    "win64": "win-x64",
    "mac64": "osx-x64",
    "macarm": "osx-arm64",
}
TARGET_ALIASES = {"mac": ["mac64", "macarm"]}


def host_platform() -> str:
    platform = detect_platform()
    if platform is None:
        raise AetherError(f"`aether run` doesn't support {sys.platform}.")
    return platform


CONFIGURATIONS = ("Debug", "Release")


def output_dir(configuration: str) -> Path:
    """Where `dotnet build` puts the client (framework-dependent, for this OS)."""
    return paths.REPO / "bin" / configuration / TARGET_FRAMEWORK


def latest_build() -> Path:
    """The most recently built client folder (Debug or Release)."""
    builds = [
        d for c in CONFIGURATIONS if ((d := output_dir(c)) / CLIENT_DLL).is_file()
    ]
    if not builds:
        raise AetherError(
            "there's no build yet; run `./aether build` (or `./aether run`)."
        )
    return max(builds, key=lambda d: (d / CLIENT_DLL).stat().st_mtime)


def apphost(platform: str) -> str:
    """The client's native launcher (the .NET apphost) on platform."""
    return "Terraria.exe" if platform == "windows" else "Terraria"


def check_build(client: Path, platform: str) -> None:
    """Refuses a client folder that can't run on platform (its apphost is built for one OS)."""
    if not (client / CLIENT_DLL).is_file():
        raise AetherError(
            f"{client} has no {CLIENT_DLL}; rebuild with `./aether build`."
        )
    if not (client / apphost(platform)).is_file():
        raise AetherError(
            f"{client} has no {apphost(platform)}: it was built on another OS. Rebuild it "
            "here with `./aether build`."
        )


def resolve_target(target: str) -> list[str]:
    """Target names for `--target` (an alias, a target, or "all")."""
    if target == "all":
        return list(TARGETS)
    if target in TARGET_ALIASES:
        return list(TARGET_ALIASES[target])
    if target not in TARGETS:
        names = ", ".join([*TARGETS, *TARGET_ALIASES, "all"])
        raise AetherError(f"unknown target {target!r}; use one of: {names}.")
    return [target]


# No lingering MSBuild nodes or compiler server: they would inherit the captured output pipe and
# keep it open after the build finishes.
DOTNET_FLAGS = ("-nodeReuse:false", "-p:UseSharedCompilation=false")


def build_command(configuration: str) -> list[str]:
    return [
        "dotnet",
        "build",
        str(paths.REPO / "Terraria.csproj"),
        "-c",
        configuration,
        *DOTNET_FLAGS,
    ]


def publish_command(configuration: str, runtime: str, dest: Path) -> list[str]:
    return [
        "dotnet",
        "publish",
        str(paths.REPO / "Terraria.csproj"),
        "-c",
        configuration,
        "-r",
        runtime,
        "--self-contained",
        "-o",
        str(dest),
        *DOTNET_FLAGS,
    ]


def run_dotnet(command: list[str], what: str) -> None:
    result = subprocess.run(command, check=False, capture_output=True, text=True)
    if result.returncode != 0:
        errors = [line for line in result.stdout.splitlines() if " error " in line]
        raise AetherError(
            f"{what} failed:\n"
            + "\n".join(dict.fromkeys(errors) or [result.stdout[-2000:]])
        )


def require_net_fna() -> None:
    """Refuses lib/'s FNA if it's the .NET Framework build from before the move to .NET 10."""
    fna = load_fna_json()
    built = paths.FNA_CACHE / fna["commit"] / FNA_OUTPUT_DIR / "FNA.dll"
    if not built.is_file() or not filecmp.cmp(
        built, paths.LIB / "FNA.dll", shallow=False
    ):
        raise AetherError(
            "lib/FNA.dll isn't FNA's .NET build; run `./aether setup` to rebuild it."
        )


def build_client(configuration: str, platform: str) -> Path:
    """Builds Terraria.csproj (and ReLogic) and returns the client folder."""
    require_net_fna()
    log(f"building the client ({configuration}, {platform})…")
    run_dotnet(build_command(configuration), "build")
    client = output_dir(configuration)
    if not (client / CLIENT_DLL).is_file():
        raise AetherError(f"the build didn't produce {client / CLIENT_DLL}")
    return client


def publish_client(configuration: str, target: str) -> Path:
    """Publishes the client self-contained for target's runtime and returns that folder."""
    runtime = RUNTIMES[target]
    dest = paths.STATE / "publish" / runtime
    if dest.exists():
        shutil.rmtree(dest)
    log(f"publishing the client for {runtime} ({configuration}, self-contained)…")
    run_dotnet(publish_command(configuration, runtime, dest), "publish")
    return dest


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
        shutil.copytree(src, dst, symlinks=True)
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


def copy_client(client: Path, dest: Path) -> None:
    """The client folder's files (apphost, Terraria.dll, its runtime files, FNA, ...) into dest."""
    dest.mkdir(parents=True, exist_ok=True)
    shutil.copytree(client, dest, symlinks=True, dirs_exist_ok=True)


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


def is_mono_file(name: str) -> bool:
    """Whether a Linux game folder entry belongs to its Mono runtime (MonoKickstart, Mono's
    class libraries and configs, the game's exe and FNA), which the client doesn't use."""
    return name in MONO_FILES or name.endswith((".dll", ".dll.config"))


def prepare_linux_server(game_dir: Path, run_dir: Path, copy: bool) -> None:
    """The vanilla server in run_dir/server/: the game's layout, with its own Mono and FNA."""
    server = run_dir / SERVER_DIR
    server.mkdir()
    for entry in game_dir.iterdir():
        if entry.name in (*LINUX_LAUNCHERS, "Terraria.exe", "Content"):
            continue
        if entry.name in LINUX_SERVER[:2]:
            shutil.copy2(entry, server / entry.name)
        else:
            place(entry, server / entry.name, copy)
    (server / "Content").symlink_to(Path("..") / "Content")


def prepare_linux(
    game_dir: Path, client: Path, run_dir: Path, copy: bool = False
) -> None:
    for launcher in LINUX_LAUNCHERS:
        if not (game_dir / launcher).is_file():
            raise AetherError(f"{game_dir} has no {launcher}; is it the Linux build?")
    natives = require_fna("fnalibs/lib64")
    warn_if_unpinned(game_dir, "linux")
    fresh_dir(run_dir)
    for entry in game_dir.iterdir():
        if not is_mono_file(entry.name) and entry.name not in (*LINUX_SERVER, "lib64"):
            place(entry, run_dir / entry.name, copy)
    # Natives next to the client: the game's (Steam, file dialogs), then Aether's FNA ones.
    for lib in (game_dir / "lib64").iterdir() if (game_dir / "lib64").is_dir() else []:
        place(lib, run_dir / lib.name, copy)
    replace_natives(natives, run_dir)
    copy_client(client, run_dir)
    prepare_linux_server(game_dir, run_dir, copy)
    stage_linux_backend(run_dir)


MAC_LAUNCHER = """#!/bin/sh
# Aether: the client is in Contents/Client/ (with its natives and Content/ link).
cd "$(dirname "$0")/../Client" || exit 1
exec ./Terraria "$@"
"""


def prepare_macos(
    platform_dir: Path, client: Path, run_dir: Path, copy: bool = False
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
            # The vanilla server's launcher and natives, as real files; the client's
            # launcher replaced by a script that starts Contents/Client/Terraria.
            shutil.copytree(entry, out / "MacOS", symlinks=True)
            launcher = out / "MacOS" / "Terraria"
            launcher.unlink(missing_ok=True)
            launcher.write_text(MAC_LAUNCHER)
            launcher.chmod(0o755)
        elif entry.name == "_CodeSignature":
            continue  # the copy isn't the signed bundle anymore
        elif entry.is_file():
            shutil.copy2(entry, out / entry.name)
        else:
            place(entry, out / entry.name, copy)
    client_dir = run_dir / MAC_CLIENT
    client_dir.mkdir()
    # Natives next to the client: the game's (Steam, file dialogs), then Aether's FNA ones.
    for lib in (contents / "MacOS" / "osx").iterdir():
        if lib.name.endswith(".dylib"):
            shutil.copy2(lib, client_dir / lib.name)
    replace_natives(natives, client_dir)
    (client_dir / "Content").symlink_to(Path("..") / "Resources" / "Content")
    steam_appid = contents / "Resources" / "steam_appid.txt"
    if steam_appid.is_file():
        shutil.copy2(steam_appid, client_dir / "steam_appid.txt")
    copy_client(client, client_dir)


def prepare_windows(
    game_dir: Path,
    platform_dir: Path | None,
    client: Path,
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
    # Next to the client, so it loads this 64-bit Steamworks.NET instead of the Linux one
    # embedded in it.
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
    copy_client(client, run_dir)


def prepare_run_dir(
    platform: str,
    game_dir: Path,
    platform_dir: Path | None,
    client: Path,
    run_dir: Path,
    copy: bool = False,
) -> None:
    """(Re)creates run_dir for platform from the client folder. game_dir is the Linux base;
    platform_dir the macOS/Windows build (unused on Linux, optional on Windows). copy makes it
    self-contained."""
    if platform == "linux":
        prepare_linux(game_dir, client, run_dir, copy)
    elif platform == "macos":
        if platform_dir is None:
            raise AetherError(
                "the macOS layout needs the macOS build (--platform-dir)."
            )
        prepare_macos(platform_dir, client, run_dir, copy)
    elif platform == "windows":
        prepare_windows(game_dir, platform_dir, client, run_dir, copy)
    else:
        raise AetherError(f"unknown platform {platform}")


def package(
    target: str, configuration: str, game_dir: Path, platform_dir: Path | None
) -> Path:
    """Builds the self-contained dist/<target>/ (lin64, win64, mac64 or macarm)."""
    client = publish_client(configuration, target)
    dest = paths.DIST / target
    log(
        f"packaging {target} into {dest.relative_to(paths.REPO)}/ (copies the game files)…"
    )
    prepare_run_dir(TARGETS[target], game_dir, platform_dir, client, dest, copy=True)
    return dest


def launcher(platform: str, run_dir: Path) -> Path:
    if platform == "macos":
        return run_dir / MAC_CONTENTS / "MacOS" / "Terraria"
    return run_dir / apphost(platform)


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
