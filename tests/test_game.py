import json
import os
from pathlib import Path

import pytest

from aether import game, paths
from aether.util import AetherError

from .conftest import write_files


@pytest.fixture
def builds(repo, tmp_path):
    """Fake Linux/macOS/Windows game builds, Aether's lib/ with FNA, and a built client."""
    paths.BASE_JSON.write_text(
        json.dumps(
            {
                **repo.pinned,
                "steam": {"app": 105600, "depot": 105602, "manifest": "1"},
                "platforms": {
                    "macos": {
                        "depot": 105603,
                        "manifest": "2",
                        "exe": "Terraria.app/Contents/Resources/Terraria.exe",
                        "exe_sha256": "0" * 64,
                    },
                    "windows": {
                        "depot": 105601,
                        "manifest": "3",
                        "exe": "Terraria.exe",
                        "exe_sha256": "0" * 64,
                    },
                },
            }
        )
    )
    linux = tmp_path / "linux"
    write_files(
        linux,
        {
            "Terraria": "#!/bin/sh\n",
            "Terraria.bin.x86_64": b"\x7fELF launcher",
            "Terraria.exe": b"vanilla exe",
            "FNA.dll": b"fna 26.3",
            "FNA.dll.config": "<old/>",
            "steam_appid.txt": "105600",
            "Content/Images/Item_1.xnb": b"linux xnb",
            "lib64/libFNA3D.so.0": b"old fna3d",
            "lib64/libsteam_api.so": b"steam",
        },
    )
    mac = tmp_path / "macos"
    write_files(
        mac,
        {
            "Terraria.app/Contents/Info.plist": "<plist/>",
            "Terraria.app/Contents/_CodeSignature/CodeResources": "sig",
            "Terraria.app/Contents/MacOS/Terraria": "#!/bin/bash\n",
            "Terraria.app/Contents/MacOS/Terraria.bin.osx": b"kickstart",
            "Terraria.app/Contents/MacOS/osx/libFNA3D.0.dylib": b"old fna3d",
            "Terraria.app/Contents/MacOS/osx/libsteam_api.dylib": b"steam",
            "Terraria.app/Contents/Resources/Terraria.exe": b"mac exe",
            "Terraria.app/Contents/Resources/FNA.dll": b"fna 26.3",
            "Terraria.app/Contents/Resources/Content/Images/Item_1.xnb": b"mac xnb",
            "Terraria.app/Contents/Resources/mscorlib.dll": b"mono",
        },
    )
    windows = tmp_path / "windows"
    write_files(
        windows,
        {
            "Terraria.exe": b"xna exe",
            "TerrariaServer.exe": b"windows server",
            "steam_api.dll": b"steam x86",
            "nfd.dll": b"nfd",
            "ReLogic.Native.dll": b"relogic native",
            "Content/Images/Item_1.xnb": b"xna reach xnb",
        },
    )
    write_files(
        paths.LIB,
        {
            "FNA.dll": b"fna 26.11",
            "FNA.dll.config": "<new/>",
            "fnalibs/lib64/libFNA3D.so.0": b"new fna3d",
            "fnalibs/x64/FNA3D.dll": b"new fna3d x64",
            "fnalibs/x64/SDL3.dll": b"new sdl3 x64",
            "fnalibs/D3D12/D3D12Core.dll": b"agility sdk",
            "steamworks/Windows-x64/Steamworks.NET.dll": b"steamworks x64",
            "steamworks/Windows-x64/steam_api64.dll": b"steam x64",
            "fnalibs-apple/osx/libFNA3D.0.dylib": b"new fna3d mac",
            "fnalibs-apple/osx/libSDL3.0.dylib": b"new sdl3 mac",
        },
    )
    exe = tmp_path / "bin" / "Terraria.exe"
    write_files(exe.parent, {"Terraria.exe": b"aether client", "Terraria.pdb": b"pdb"})
    return linux, mac, windows, exe, repo.root / ".aether" / "run"


def assert_client(dest: Path) -> None:
    assert (dest / "Terraria.exe").read_bytes() == b"aether client"
    assert (dest / "Terraria.pdb").read_bytes() == b"pdb"
    assert (dest / "FNA.dll").read_bytes() == b"fna 26.11"
    assert not (dest / "FNA.dll").is_symlink()


# --- Linux ------------------------------------------------------------------


def test_linux_links_the_game_and_copies_the_client(builds):
    linux, _, _, exe, run_dir = builds
    game.prepare_run_dir("linux", linux, Path(), exe, run_dir)

    # Launchers must be real files: MonoKickstart finds lib64/ next to its real path.
    for launcher in ("Terraria", "Terraria.bin.x86_64"):
        assert not (run_dir / launcher).is_symlink()
        assert (run_dir / launcher).read_bytes() == (linux / launcher).read_bytes()
    assert (run_dir / "Content").is_symlink()
    assert (run_dir / "Content/Images/Item_1.xnb").read_bytes() == b"linux xnb"
    assert_client(run_dir)
    lib64 = run_dir / "lib64"
    assert (lib64 / "libFNA3D.so.0").read_bytes() == b"new fna3d"
    assert not (lib64 / "libFNA3D.so.0").is_symlink()
    assert (lib64 / "libsteam_api.so").is_symlink()


def test_linux_warns_about_an_unpinned_build(builds, capsys):
    linux, _, _, exe, run_dir = builds
    game.prepare_run_dir("linux", linux, Path(), exe, run_dir)
    err = capsys.readouterr().err
    assert "isn't the pinned linux build" in err
    assert "`./aether download`" in err


def test_run_dir_is_rebuilt_from_scratch(builds):
    linux, _, _, exe, run_dir = builds
    game.prepare_run_dir("linux", linux, Path(), exe, run_dir)
    (run_dir / "stale.txt").write_text("left over")
    game.prepare_run_dir("linux", linux, Path(), exe, run_dir)
    assert not (run_dir / "stale.txt").exists()


def test_linux_needs_aethers_fna(builds):
    linux, _, _, exe, run_dir = builds
    (paths.LIB / "FNA.dll").unlink()
    with pytest.raises(AetherError, match="aether setup"):
        game.prepare_run_dir("linux", linux, Path(), exe, run_dir)


def test_linux_needs_the_linux_build(builds):
    _, mac, _, exe, run_dir = builds
    with pytest.raises(AetherError, match="Linux build"):
        game.prepare_run_dir("linux", mac, Path(), exe, run_dir)


# --- macOS ------------------------------------------------------------------


def test_macos_copies_the_bundle_runtime_and_links_resources(builds, capsys):
    _, mac, _, exe, run_dir = builds
    game.prepare_run_dir("macos", Path(), mac, exe, run_dir)
    contents = run_dir / "Terraria.app" / "Contents"

    assert (contents / "Info.plist").read_text() == "<plist/>"
    assert not (contents / "_CodeSignature").exists()
    macos = contents / "MacOS"
    assert not macos.is_symlink()
    assert not (macos / "Terraria.bin.osx").is_symlink()
    assert (macos / "osx/libFNA3D.0.dylib").read_bytes() == b"new fna3d mac"
    assert (macos / "osx/libSDL3.0.dylib").read_bytes() == b"new sdl3 mac"
    assert (macos / "osx/libsteam_api.dylib").read_bytes() == b"steam"

    resources = contents / "Resources"
    assert (resources / "Content").is_symlink()
    assert (resources / "Content/Images/Item_1.xnb").read_bytes() == b"mac xnb"
    assert (resources / "mscorlib.dll").is_symlink()
    assert_client(resources)
    assert "isn't the pinned macos build" in capsys.readouterr().err


def test_macos_needs_the_macos_build(builds):
    linux, _, _, exe, run_dir = builds
    with pytest.raises(AetherError, match="macOS build"):
        game.prepare_run_dir("macos", Path(), linux, exe, run_dir)


# --- Windows ----------------------------------------------------------------


def test_windows_combines_linux_content_and_64_bit_runtime(builds):
    linux, _, windows, exe, run_dir = builds
    game.prepare_run_dir("windows", linux, windows, exe, run_dir)

    # FNA needs the Linux build's Content; the Windows build's is XNA's.
    assert (run_dir / "Content/Images/Item_1.xnb").read_bytes() == b"linux xnb"
    assert (run_dir / "steam_appid.txt").read_text() == "105600"
    for name, data in (
        ("TerrariaServer.exe", b"windows server"),
        ("steam_api.dll", b"steam x86"),  # the server's, it's 32-bit
        ("Steamworks.NET.dll", b"steamworks x64"),
        ("steam_api64.dll", b"steam x64"),
        ("FNA3D.dll", b"new fna3d x64"),
        ("SDL3.dll", b"new sdl3 x64"),
        ("D3D12/D3D12Core.dll", b"agility sdk"),
    ):
        assert (run_dir / name).read_bytes() == data
    # The Windows build's 32-bit natives would fail to load in a 64-bit process.
    assert not (run_dir / "nfd.dll").exists()
    assert not (run_dir / "ReLogic.Native.dll").exists()
    assert_client(run_dir)
    assert not (run_dir / "lib64").exists()


def test_windows_works_without_the_windows_build(builds, capsys):
    linux, _, _, exe, run_dir = builds
    game.prepare_run_dir("windows", linux, None, exe, run_dir)
    assert_client(run_dir)
    assert (run_dir / "steam_api64.dll").is_file()
    assert not (run_dir / "TerrariaServer.exe").exists()
    assert "Host & Play won't work" in capsys.readouterr().err


def test_windows_needs_the_linux_content(builds):
    _, _, windows, exe, run_dir = builds
    with pytest.raises(AetherError, match="Linux build"):
        game.prepare_run_dir("windows", windows, windows, exe, run_dir)


def test_windows_needs_64_bit_steamworks(builds):
    linux, _, windows, exe, run_dir = builds
    (paths.LIB / "steamworks/Windows-x64/steam_api64.dll").unlink()
    with pytest.raises(AetherError, match="aether setup"):
        game.prepare_run_dir("windows", linux, windows, exe, run_dir)


# --- self-contained targets -------------------------------------------------


@pytest.mark.parametrize(
    ("target", "expected"),
    [
        ("lin64", ["lin64"]),
        ("win64", ["win64"]),
        ("mac", ["mac"]),
        ("mac64", ["mac"]),
        ("macarm", ["mac"]),
        ("all", ["lin64", "win64", "mac"]),
    ],
)
def test_resolve_target(target, expected):
    assert game.resolve_target(target) == expected


def test_resolve_target_rejects_unknown_names():
    with pytest.raises(AetherError, match="unknown target 'win32'"):
        game.resolve_target("win32")


def no_symlinks(root: Path) -> list[Path]:
    return [p for p in root.rglob("*") if p.is_symlink()]


@pytest.mark.parametrize(
    ("target", "launcher"),
    [
        ("lin64", "Terraria"),
        ("win64", "Terraria.exe"),
        ("mac", "Terraria.app/Contents/MacOS/Terraria"),
    ],
)
def test_package_copies_everything(builds, target, launcher):
    linux, mac, windows, exe, _ = builds
    platform_dir = {"lin64": None, "win64": windows, "mac": mac}[target]
    dest = game.package(target, exe, linux, platform_dir)

    assert dest == paths.DIST / target
    assert (dest / launcher).is_file()
    assert no_symlinks(dest) == []
    client = dest / "Terraria.app/Contents/Resources" if target == "mac" else dest
    assert_client(client)
    content = client / "Content/Images/Item_1.xnb"
    assert content.read_bytes() == (b"mac xnb" if target == "mac" else b"linux xnb")


def test_package_replaces_the_previous_folder(builds):
    linux, _, _, exe, _ = builds
    dest = game.package("lin64", exe, linux, None)
    (dest / "stale.txt").write_text("left over")
    game.package("lin64", exe, linux, None)
    assert not (dest / "stale.txt").exists()


# --- building and launching -------------------------------------------------


def test_one_anycpu_build_for_every_os():
    """Every target runs the same Terraria.exe, so nothing may pin it to a bitness."""
    assert "PlatformTarget" not in (paths.REPO / "Terraria.csproj").read_text()
    assert "PlatformTarget" not in " ".join(game.build_command("Debug"))
    assert "-nodeReuse:false" in game.build_command("Release")


@pytest.mark.parametrize(
    ("platform", "launcher"),
    [
        ("linux", "Terraria"),
        ("macos", "Terraria.app/Contents/MacOS/Terraria"),
        ("windows", "Terraria.exe"),
    ],
)
def test_launch_command(tmp_path, platform, launcher):
    run_dir, saves = tmp_path / "run", tmp_path / "saves"
    assert game.launch_command(platform, run_dir, saves, ["-world", "w.wld"]) == [
        str(run_dir / launcher),
        "-savedirectory",
        str(saves),
        "-world",
        "w.wld",
    ]
    assert game.launch_command(platform, run_dir, None, []) == [str(run_dir / launcher)]


@pytest.mark.parametrize(
    ("sys_platform", "expected"),
    [("linux", "linux"), ("darwin", "macos"), ("win32", "windows")],
)
def test_host_platform(monkeypatch, sys_platform, expected):
    monkeypatch.setattr(game.sys, "platform", sys_platform)
    assert game.host_platform() == expected


def test_host_platform_rejects_others(monkeypatch):
    monkeypatch.setattr(game.sys, "platform", "freebsd14")
    with pytest.raises(AetherError, match="doesn't support"):
        game.host_platform()


def test_relative_save_dir_is_resolved_against_the_callers_directory(
    repo, tmp_path, monkeypatch
):
    """The game runs inside .aether/run/, which every run wipes; saves must not land there."""
    from typer.testing import CliRunner

    from aether import cli

    seen = {}
    exe = tmp_path / "Terraria.exe"
    exe.write_bytes(b"client")
    monkeypatch.setattr(cli, "host_platform", lambda: "linux")
    monkeypatch.setattr(cli, "latest_build", lambda: exe)
    monkeypatch.setattr(cli, "prepare_run_dir", lambda *args: None)
    monkeypatch.setattr(
        cli,
        "launch",
        lambda platform, run_dir, saves, args: seen.update(saves=saves, args=args) or 0,
    )
    monkeypatch.chdir(tmp_path)
    result = CliRunner().invoke(
        cli.app, ["launch", "--save-dir", "mysaves", "--", "-world", "my world.wld"]
    )
    assert result.exit_code == 0, result.output
    assert seen == {"saves": tmp_path / "mysaves", "args": ["-world", "my world.wld"]}


def test_latest_build_picks_the_newest_configuration(repo):
    debug, release = game.output_exe("Debug"), game.output_exe("Release")
    with pytest.raises(AetherError, match="aether build"):
        game.latest_build()
    for exe, mtime in ((debug, 100), (release, 200)):
        exe.parent.mkdir(parents=True)
        exe.write_bytes(b"exe")
        os.utime(exe, (mtime, mtime))
    assert game.latest_build() == release
    os.utime(debug, (300, 300))
    assert game.latest_build() == debug


@pytest.mark.parametrize(
    ("flags", "ok_on_windows"),
    [(0x1, True), (0x3, False), (0x20001, False)],  # AnyCPU, x86, AnyCPU32BitPreferred
)
def test_windows_refuses_a_32_bit_build(tmp_path, monkeypatch, flags, ok_on_windows):
    monkeypatch.setattr(game, "clr_flags", lambda exe: flags)
    exe = tmp_path / "Terraria.exe"
    game.check_build(exe, "linux")
    game.check_build(exe, "macos")
    if ok_on_windows:
        game.check_build(exe, "windows")
    else:
        with pytest.raises(AetherError, match="is a 32-bit build"):
            game.check_build(exe, "windows")


def test_clr_flags_reads_real_executables():
    exe = Path.home() / ".local/share/Steam/steamapps/common/Terraria/Terraria.exe"
    if not exe.is_file():
        pytest.skip("no Terraria install")
    assert game.clr_flags(exe) & 0x1  # IL-only
    assert not game.runs_32_bit(exe)  # the Linux build is AnyCPU


def test_clr_flags_rejects_non_pe_files(tmp_path):
    junk = tmp_path / "junk.exe"
    junk.write_bytes(b"not a PE file at all" * 10)
    with pytest.raises(AetherError, match="isn't a .NET executable"):
        game.clr_flags(junk)


def test_build_command_prints_the_output(repo, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    monkeypatch.setattr(cli, "host_platform", lambda: "windows")
    built = []
    monkeypatch.setattr(
        cli,
        "build_client",
        lambda configuration, platform: (
            built.append((configuration, platform)) or game.output_exe(configuration)
        ),
    )
    result = CliRunner().invoke(cli.app, ["build", "--release"])
    assert result.exit_code == 0, result.output
    assert built == [("Release", "windows")]
    assert "bin/Release/net40/Terraria.exe" in result.output.replace("\\", "/")


def test_build_packages_targets(repo, tmp_path, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    exe = game.output_exe("Debug")
    linux, mac = tmp_path / "linux", tmp_path / "mac"
    monkeypatch.setattr(cli, "host_platform", lambda: "linux")
    monkeypatch.setattr(cli, "build_client", lambda configuration, platform: exe)
    monkeypatch.setattr(cli, "check_build", lambda exe, platform: None)
    monkeypatch.setattr(cli, "default_game_dir", lambda: linux)
    monkeypatch.setattr(cli, "platform_game_dir", lambda platform: tmp_path / platform)
    packaged = []
    monkeypatch.setattr(
        cli,
        "package",
        lambda target, exe, game_dir, platform_dir: (
            packaged.append((target, game_dir, platform_dir)) or paths.DIST / target
        ),
    )
    result = CliRunner().invoke(
        cli.app, ["build", "--target", "macarm", "--target", "all"]
    )
    assert result.exit_code == 0, result.output
    assert packaged == [
        ("mac", linux, tmp_path / "macos"),
        ("lin64", linux, None),
        ("win64", linux, tmp_path / "windows"),
    ]

    packaged.clear()
    result = CliRunner().invoke(
        cli.app, ["build", "--target", "mac64", "--platform-dir", str(mac)]
    )
    assert result.exit_code == 0, result.output
    assert packaged == [("mac", linux, mac)]


def test_build_platform_dir_needs_one_non_linux_target(repo, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    monkeypatch.setattr(cli, "build_client", lambda *args: pytest.fail("built"))
    result = CliRunner().invoke(
        cli.app, ["build", "--target", "all", "--platform-dir", "x"]
    )
    assert result.exit_code != 0
    assert isinstance(result.exception, AetherError)
