"""Downloading the pinned Linux build of Terraria (the base) with SteamCMD.

`download_depot` fetches the exact manifest pinned in tools/base.json, so the result always
matches the pinned Terraria.exe; `app_update` would fetch whatever build is current. SteamCMD
prompts for the password and Steam Guard code itself; Aether only passes the username.
"""

import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.request
import zipfile
from pathlib import Path

from . import paths
from .pinning import load_base_json, save_base_json
from .util import AetherError, host_platform, log, sha256_file

_INSTALLER = "https://steamcdn-a.akamaihd.net/client/installer"
# Valve's SteamCMD per host OS: (archive, launcher inside it).
STEAMCMD_BUILDS = {
    "linux": (f"{_INSTALLER}/steamcmd_linux.tar.gz", "steamcmd.sh"),
    "macos": (f"{_INSTALLER}/steamcmd_osx.tar.gz", "steamcmd.sh"),
    "windows": (f"{_INSTALLER}/steamcmd.zip", "steamcmd.exe"),
}
LAUNCHERS = ("steamcmd.sh", "steamcmd.exe")
# How far below a SteamCMD folder to look for its downloads. Linux keeps them in
# linux32/steamapps/content/, Windows in steamapps/content/, and macOS below its self-updated
# Steam.AppBundle/; installed games (common/) are skipped.
DEPOT_SEARCH_DEPTH = 10
DEPOT_SEARCH_SKIP = {"common", "downloading", "shadercache", "workshop", "temp"}
# Launchers that must stay executable after moving the depot (Linux, then macOS).
EXECUTABLES = (
    "Terraria",
    "Terraria.bin.x86_64",
    "TerrariaServer",
    "TerrariaServer.bin.x86_64",
    "Terraria.app/Contents/MacOS/Terraria",
    "Terraria.app/Contents/MacOS/Terraria.bin.osx",
    "Terraria.app/Contents/MacOS/TerrariaServer",
    "Terraria.app/Contents/MacOS/TerrariaServer.bin.osx",
)
# The Linux build is the base; the macOS and Windows builds only supply runtime files
# (natives, launchers, platform Content) for running the client on those systems.
PLATFORMS = ("linux", "macos", "windows")


def steamcmd_roots() -> list[Path]:
    """Folders where SteamCMD is commonly installed (its downloads land below them)."""
    roots = [Path.home() / ".steam" / "steamcmd", paths.STEAMCMD_DIR]
    if host_platform() == "macos":
        roots.append(Path.home() / "Library" / "Application Support" / "Steam")
    elif host_platform() == "windows":
        # Package managers put a shim on PATH; SteamCMD itself (and its downloads) live here.
        choco = Path(os.environ.get("ChocolateyInstall", r"C:\ProgramData\chocolatey"))
        roots += [
            choco / "lib" / "steamcmd" / "tools",
            Path.home() / "scoop" / "apps" / "steamcmd" / "current",
        ]
    return roots


def find_steamcmd() -> list[str] | None:
    """The command that runs SteamCMD, or None if there's no SteamCMD installed."""
    exe = shutil.which("steamcmd")  # also finds steamcmd.exe on Windows
    if exe:
        return [exe]
    for root in steamcmd_roots():
        for launcher in LAUNCHERS:
            if (root / launcher).is_file():
                return [str(root / launcher)]
    return None


def bootstrap_steamcmd() -> list[str]:
    """Installs Valve's SteamCMD for this OS into .aether/steamcmd/."""
    build = STEAMCMD_BUILDS.get(host_platform() or "")
    if build is None:
        raise AetherError(
            f"no SteamCMD found, and there's no official SteamCMD for {sys.platform}."
        )
    url, launcher = build
    log(f"installing SteamCMD from {url}…")
    paths.STEAMCMD_DIR.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="aether-steamcmd-") as tmp:
        archive = Path(tmp) / url.rsplit("/", 1)[1]
        urllib.request.urlretrieve(url, archive)
        if archive.suffix == ".zip":
            with zipfile.ZipFile(archive) as zf:
                zf.extractall(paths.STEAMCMD_DIR)
        else:
            with tarfile.open(archive) as tar:
                tar.extractall(paths.STEAMCMD_DIR, filter="data")
    path = paths.STEAMCMD_DIR / launcher
    if not path.is_file():
        raise AetherError(f"the SteamCMD download from {url} has no {launcher}.")
    if launcher.endswith(".sh"):
        path.chmod(path.stat().st_mode | 0o111)
    return [str(path)]


def download_args(username: str, steam: dict) -> list[str]:
    return [
        "+login",
        username,
        "+download_depot",
        str(steam["app"]),
        str(steam["depot"]),
        str(steam["manifest"]),
        "+quit",
    ]


def search_roots(cmd: list[str]) -> list[Path]:
    """Where the given SteamCMD may have put its downloads: its own folder first."""
    roots = [Path(cmd[0]).resolve().parent, *steamcmd_roots()]
    return [r for i, r in enumerate(roots) if r.is_dir() and r not in roots[:i]]


def find_depot_dirs(root: Path, app: int, depot: int) -> list[Path]:
    """Folders below root ending in steamapps/content/app_<app>/depot_<depot>."""
    tail = ("steamapps", "content", f"app_{app}", f"depot_{depot}")
    found = []
    for dirpath, dirnames, _ in os.walk(root):
        here = Path(dirpath)
        if here.parts[-4:] == tail:
            found.append(here)
            dirnames[:] = []
        elif len(here.relative_to(root).parts) >= DEPOT_SEARCH_DEPTH:
            dirnames[:] = []
        else:
            dirnames[:] = [d for d in dirnames if d not in DEPOT_SEARCH_SKIP]
    return found


def parse_keyvalues(text: str) -> dict:
    """Parses Valve's text KeyValues ("key" "value" / "key" { ... }) into nested dicts."""
    tokens = re.findall(r'"((?:[^"\\]|\\.)*)"|([{}])', text)
    root: dict = {}
    stack = [root]
    key = None
    for quoted, brace in tokens:
        if brace == "{":
            child: dict = {}
            stack[-1][key] = child
            stack.append(child)
            key = None
        elif brace == "}":
            if len(stack) > 1:
                stack.pop()
        elif key is None:
            key = quoted
        else:
            stack[-1][key] = quoted
            key = None
    return root


def app_info(cmd: list[str], app: int) -> dict:
    """Steam's public app info for app, via an anonymous SteamCMD login (no credentials)."""
    result = subprocess.run(
        [
            *cmd,
            "+login",
            "anonymous",
            "+app_info_update",
            "1",
            "+app_info_print",
            str(app),
            "+quit",
        ],
        check=False,
        capture_output=True,
        text=True,
        errors="replace",
    )
    start = result.stdout.find(f'"{app}"')
    if start < 0:
        raise AetherError(
            f"SteamCMD printed no app info for {app}:\n{result.stdout[-1500:]}"
        )
    info = parse_keyvalues(result.stdout[start:]).get(str(app))
    if not isinstance(info, dict):
        raise AetherError(f"couldn't parse SteamCMD's app info for {app}.")
    return info


def latest_manifests(info: dict, depots: list[int]) -> dict[int, str]:
    """The public branch's manifest of each depot."""
    found = {}
    for depot in depots:
        try:
            found[depot] = info["depots"][str(depot)]["manifests"]["public"]["gid"]
        except (KeyError, TypeError) as e:
            raise AetherError(
                f"Steam's app info has no public manifest for depot {depot}."
            ) from e
    return found


def platform_pin(pinned: dict, platform: str) -> dict:
    """A build's depot pin: app, depot, manifest, and its Terraria.exe path and hash."""
    if platform == "linux":
        return {
            **pinned["steam"],
            "exe": "Terraria.exe",
            "exe_sha256": pinned["exe_sha256"],
        }
    return {"app": pinned["steam"]["app"], **pinned["platforms"][platform]}


def downloaded_dir(platform: str) -> Path:
    """Where `aether download --platform <platform>` puts that build."""
    if platform == "linux":
        return paths.DOWNLOADED_GAME_DIR
    return paths.STATE / f"game-{platform}"


def find_downloaded_depot(
    pin: dict, roots: list[Path], since: float = 0.0
) -> Path | None:
    """The newest downloaded depot folder that holds the pinned build.

    Without a pinned hash (a manifest `aether update` just found) only folders written
    since the download started count, so a leftover older download isn't mistaken for it.
    """
    candidates = [
        d
        for root in roots
        for d in find_depot_dirs(root, pin["app"], pin["depot"])
        if is_pinned_game(d, pin)
        and (pin.get("exe_sha256") or (d / pin["exe"]).stat().st_mtime >= since)
    ]
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def is_pinned_game(game_dir: Path, pin: dict) -> bool:
    """Whether game_dir holds the pinned build. A pin without a hash yet (just updated to a
    new manifest, not downloaded since) only requires the exe to exist."""
    exe = game_dir / pin["exe"]
    if not exe.is_file():
        return False
    return pin.get("exe_sha256") is None or sha256_file(exe) == pin["exe_sha256"]


def download_game(
    username: str,
    dest: Path | None = None,
    force: bool = False,
    platform: str = "linux",
    pin: dict | None = None,
) -> Path:
    """Downloads a pinned build (the Linux base by default) with SteamCMD; returns its folder.

    pin overrides tools/base.json's (e.g. a newer manifest during `aether update`).
    """
    pinned = load_base_json()
    pin = pin or platform_pin(pinned, platform)
    dest = dest or downloaded_dir(platform)
    if not force and is_pinned_game(dest, pin) and pin.get("exe_sha256"):
        log(f"{dest} already has the pinned {pinned['game_version']} {platform} build.")
        return dest

    cmd = find_steamcmd() or bootstrap_steamcmd()
    log(
        f"downloading Terraria for {platform} (depot {pin['depot']}, manifest "
        f"{pin['manifest']}); SteamCMD may ask for your password and Steam Guard code."
    )
    # Inherit the terminal: SteamCMD prompts for credentials itself. Its exit code isn't
    # reliable, so success is judged by the downloaded files.
    started = time.time() - 1
    subprocess.run([*cmd, *download_args(username, pin)], check=False)

    roots = search_roots(cmd)
    depot = find_downloaded_depot(pin, roots, since=started)
    if depot is None:
        searched = "\n  ".join(str(r) for r in roots)
        raise AetherError(
            "SteamCMD didn't produce the pinned build (wrong login, no ownership, or a "
            "failed download; see its output above), or it saved it outside the folders "
            f"searched:\n  {searched}"
        )
    if dest.exists():
        shutil.rmtree(dest)
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(depot), str(dest))
    for name in EXECUTABLES:
        path = dest / name
        if path.is_file():
            path.chmod(path.stat().st_mode | 0o111)
    if platform != "linux" and pin.get("exe_sha256") is None:
        record_platform_hash(platform, pin["manifest"], sha256_file(dest / pin["exe"]))
    log(f"downloaded the pinned {platform} build into {dest}.")
    return dest


def record_platform_hash(platform: str, manifest: str, exe_sha256: str) -> None:
    """Pins the exe hash of a macOS/Windows manifest the first time it's downloaded."""
    pinned = load_base_json()
    entry = pinned["platforms"][platform]
    if entry["manifest"] == manifest and entry.get("exe_sha256") is None:
        entry["exe_sha256"] = exe_sha256
        save_base_json(pinned)
        log(f"pinned the {platform} build's Terraria.exe hash in tools/base.json.")


def default_game_dir() -> Path:
    """The Linux base: `aether download`'s copy if there is one, otherwise (on Linux only)
    the Steam install. On macOS/Windows the Steam install is a different build."""
    if (paths.DOWNLOADED_GAME_DIR / "Terraria.exe").is_file():
        return paths.DOWNLOADED_GAME_DIR
    if host_platform() != "linux":
        return paths.DOWNLOADED_GAME_DIR
    return paths.DEFAULT_GAME_DIR


def platform_game_dir(platform: str) -> Path:
    """A macOS/Windows build: the downloaded one, else the Steam install if this OS is that
    platform (the Steam install of another OS's build isn't on this machine)."""
    downloaded = downloaded_dir(platform)
    pin = platform_pin(load_base_json(), platform)
    if (downloaded / pin["exe"]).is_file() or host_platform() != platform:
        return downloaded
    return paths.DEFAULT_GAME_DIR
