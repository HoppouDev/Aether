import hashlib
import json
import os
import stat
import tarfile
import zipfile

import pytest

from aether import paths, steam
from aether.util import AetherError

FAKE_EXE = b"pinned Terraria.exe"
STEAM_PIN = {"app": 105600, "depot": 105602, "manifest": "4206021387845829879"}
CONTENT = "steamapps/content/app_105600/depot_105602"
LAYOUTS = {
    "linux": f"linux32/{CONTENT}",
    "windows": CONTENT,
    "macos": f"Steam.AppBundle/Steam/Contents/MacOS/{CONTENT}",
}


@pytest.fixture(params=sorted(LAYOUTS))
def fake_steamcmd(request, repo, tmp_path, monkeypatch):
    """A stand-in SteamCMD that 'downloads' the depot in its OS's folder layout."""
    root = tmp_path / "steamcmd-root"
    root.mkdir()
    launcher = root / "steamcmd.exe"
    launcher.write_text("")
    pinned = {
        **repo.pinned,
        "exe_sha256": hashlib.sha256(FAKE_EXE).hexdigest(),
        "steam": STEAM_PIN,
    }
    paths.BASE_JSON.write_text(json.dumps(pinned))
    monkeypatch.setattr(steam, "steamcmd_roots", list)
    monkeypatch.setattr(steam, "find_steamcmd", lambda: [str(launcher)])

    calls = []
    state = {"exe": FAKE_EXE}

    def fake_run(cmd, check=False):
        calls.append(cmd[1:])
        if state["exe"] is not None:
            depot = root / LAYOUTS[request.param]
            depot.mkdir(parents=True, exist_ok=True)
            (depot / "Terraria.exe").write_bytes(state["exe"])
            (depot / "Terraria.bin.x86_64").write_bytes(b"\x7fELF")  # no exec bit

    monkeypatch.setattr(steam.subprocess, "run", fake_run)
    return calls, state


def test_download_fetches_the_pinned_manifest(repo, fake_steamcmd):
    calls, _ = fake_steamcmd
    dest = repo.root / ".aether" / "game"
    steam.download_game("someone", dest)

    assert calls == [
        [
            "+login",
            "someone",
            "+download_depot",
            "105600",
            "105602",
            "4206021387845829879",
            "+quit",
        ]
    ]
    assert (dest / "Terraria.exe").read_bytes() == FAKE_EXE
    assert os.stat(dest / "Terraria.bin.x86_64").st_mode & stat.S_IXUSR


def test_download_is_skipped_when_already_pinned(repo, fake_steamcmd):
    calls, _ = fake_steamcmd
    dest = repo.root / ".aether" / "game"
    steam.download_game("someone", dest)
    steam.download_game("someone", dest)
    assert len(calls) == 1
    steam.download_game("someone", dest, force=True)
    assert len(calls) == 2


@pytest.mark.parametrize("produced", [None, b"some other build"])
def test_download_fails_without_the_pinned_build(repo, fake_steamcmd, produced):
    _, state = fake_steamcmd
    state["exe"] = produced
    dest = repo.root / ".aether" / "game"
    with pytest.raises(AetherError, match="didn't produce the pinned build"):
        steam.download_game("someone", dest)
    assert not dest.exists()


def test_depot_search_skips_installed_games(tmp_path):
    (tmp_path / "steamapps/common/Terraria" / CONTENT).mkdir(parents=True)
    assert steam.find_depot_dirs(tmp_path, 105600, 105602) == []
    (tmp_path / CONTENT).mkdir(parents=True)
    assert steam.find_depot_dirs(tmp_path, 105600, 105602) == [tmp_path / CONTENT]


@pytest.mark.parametrize(
    ("platform", "archive", "launcher"),
    [
        ("linux", "steamcmd_linux.tar.gz", "steamcmd.sh"),
        ("darwin", "steamcmd_osx.tar.gz", "steamcmd.sh"),
        ("win32", "steamcmd.zip", "steamcmd.exe"),
    ],
)
def test_bootstrap_installs_valves_build_for_each_os(
    repo, tmp_path, monkeypatch, platform, archive, launcher
):
    monkeypatch.setattr(paths, "STEAMCMD_DIR", repo.root / ".aether" / "steamcmd")
    monkeypatch.setattr(steam.sys, "platform", platform)
    fetched = []

    def fake_urlretrieve(url, dest):
        fetched.append(url)
        src = tmp_path / "payload" / launcher
        src.parent.mkdir(exist_ok=True)
        src.write_text("#!/bin/sh\n")
        if str(dest).endswith(".zip"):
            with zipfile.ZipFile(dest, "w") as zf:
                zf.write(src, launcher)
        else:
            with tarfile.open(dest, "w:gz") as tf:
                tf.add(src, launcher)

    monkeypatch.setattr(steam.urllib.request, "urlretrieve", fake_urlretrieve)
    cmd = steam.bootstrap_steamcmd()

    assert fetched == [f"https://steamcdn-a.akamaihd.net/client/installer/{archive}"]
    assert cmd == [str(paths.STEAMCMD_DIR / launcher)]
    if launcher.endswith(".sh"):
        assert os.stat(cmd[0]).st_mode & stat.S_IXUSR


def test_bootstrap_refuses_unsupported_os(monkeypatch):
    monkeypatch.setattr(steam.sys, "platform", "freebsd14")
    with pytest.raises(AetherError, match="no official SteamCMD"):
        steam.bootstrap_steamcmd()


def test_find_steamcmd_prefers_path_then_known_roots(tmp_path, monkeypatch):
    monkeypatch.setattr(steam.shutil, "which", lambda name: "/usr/bin/steamcmd")
    assert steam.find_steamcmd() == ["/usr/bin/steamcmd"]

    monkeypatch.setattr(steam.shutil, "which", lambda name: None)
    sh_root, exe_root = tmp_path / "linux", tmp_path / "windows"
    monkeypatch.setattr(
        steam, "steamcmd_roots", lambda: [tmp_path / "missing", sh_root, exe_root]
    )
    assert steam.find_steamcmd() is None
    exe_root.mkdir()
    (exe_root / "steamcmd.exe").write_text("")
    assert steam.find_steamcmd() == [str(exe_root / "steamcmd.exe")]
    sh_root.mkdir()
    (sh_root / "steamcmd.sh").write_text("#!/bin/sh\n")
    assert steam.find_steamcmd() == [str(sh_root / "steamcmd.sh")]


def test_default_game_dir_prefers_the_downloaded_copy(repo, monkeypatch):
    monkeypatch.setattr(paths, "DOWNLOADED_GAME_DIR", repo.root / ".aether" / "game")
    monkeypatch.setattr(paths, "DEFAULT_GAME_DIR", repo.root / "steam-install")
    assert steam.default_game_dir() == paths.DEFAULT_GAME_DIR
    (paths.DOWNLOADED_GAME_DIR).mkdir(parents=True)
    (paths.DOWNLOADED_GAME_DIR / "Terraria.exe").write_bytes(b"x")
    assert steam.default_game_dir() == paths.DOWNLOADED_GAME_DIR


def test_real_base_json_pins_the_linux_depot():
    from .conftest import REAL_REPO

    pin = json.loads((REAL_REPO / "tools" / "base.json").read_text())["steam"]
    assert pin["app"] == 105600 and pin["depot"] == 105602
    assert pin["manifest"].isdigit()


def test_download_macos_build_checks_the_app_bundle_exe(repo, tmp_path, monkeypatch):
    root = tmp_path / "steamcmd-root"
    root.mkdir()
    mac_exe = b"mac Terraria.exe"
    pinned = {
        **repo.pinned,
        "steam": STEAM_PIN,
        "platforms": {
            "macos": {
                "depot": 105603,
                "manifest": "7648670563869725321",
                "exe": "Terraria.app/Contents/Resources/Terraria.exe",
                "exe_sha256": hashlib.sha256(mac_exe).hexdigest(),
            }
        },
    }
    paths.BASE_JSON.write_text(json.dumps(pinned))
    monkeypatch.setattr(steam, "steamcmd_roots", list)
    monkeypatch.setattr(steam, "find_steamcmd", lambda: [str(root / "steamcmd.sh")])
    calls = []

    def fake_run(cmd, check=False):
        calls.append(cmd[1:])
        bundle = root / "linux32/steamapps/content/app_105600/depot_105603/Terraria.app"
        (bundle / "Contents/Resources").mkdir(parents=True)
        (bundle / "Contents/MacOS").mkdir()
        (bundle / "Contents/Resources/Terraria.exe").write_bytes(mac_exe)
        (bundle / "Contents/MacOS/Terraria.bin.osx").write_bytes(b"kickstart")

    monkeypatch.setattr(steam.subprocess, "run", fake_run)
    monkeypatch.setattr(paths, "STATE", repo.root / ".aether")
    dest = steam.download_game("someone", platform="macos")

    assert dest == repo.root / ".aether" / "game-macos"
    assert calls[0][2:6] == [
        "+download_depot",
        "105600",
        "105603",
        "7648670563869725321",
    ]
    launcher = dest / "Terraria.app/Contents/MacOS/Terraria.bin.osx"
    assert os.stat(launcher).st_mode & stat.S_IXUSR


@pytest.mark.parametrize("host", ["darwin", "win32"])
def test_default_game_dir_never_falls_back_to_a_non_linux_install(
    repo, monkeypatch, host
):
    """On macOS/Windows the Steam install is that OS's build, never the Linux base."""
    monkeypatch.setattr(steam.sys, "platform", host)
    monkeypatch.setattr(paths, "DOWNLOADED_GAME_DIR", repo.root / ".aether" / "game")
    monkeypatch.setattr(paths, "DEFAULT_GAME_DIR", repo.root / "steam-install")
    assert steam.default_game_dir() == paths.DOWNLOADED_GAME_DIR


def test_windows_searches_package_manager_steamcmd_folders(monkeypatch):
    monkeypatch.setattr(steam.sys, "platform", "win32")
    monkeypatch.setenv("ChocolateyInstall", "/choco")
    roots = [str(r).replace("\\", "/") for r in steam.steamcmd_roots()]
    assert "/choco/lib/steamcmd/tools" in roots
    assert any(r.endswith("scoop/apps/steamcmd/current") for r in roots)


@pytest.mark.parametrize(
    ("host", "platform", "steam_install"),
    [
        ("darwin", "macos", True),
        ("win32", "windows", True),
        (
            "linux",
            "macos",
            False,
        ),  # packaging another OS's target: only a download works
        ("linux", "windows", False),
        ("win32", "macos", False),
    ],
)
def test_platform_game_dir(repo, monkeypatch, host, platform, steam_install):
    pins = {"exe": "Terraria.exe", "depot": 1, "manifest": "1", "exe_sha256": None}
    paths.BASE_JSON.write_text(
        json.dumps(
            {
                **repo.pinned,
                "steam": {"app": 105600},
                "platforms": {"macos": pins, "windows": pins},
            }
        )
    )
    monkeypatch.setattr(steam.sys, "platform", host)
    monkeypatch.setattr(paths, "DEFAULT_GAME_DIR", repo.root / "steam-install")
    downloaded = steam.downloaded_dir(platform)
    expected = paths.DEFAULT_GAME_DIR if steam_install else downloaded
    assert steam.platform_game_dir(platform) == expected
    (downloaded / "Terraria.exe").parent.mkdir(parents=True)
    (downloaded / "Terraria.exe").write_bytes(b"exe")
    assert steam.platform_game_dir(platform) == downloaded
