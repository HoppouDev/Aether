import json
import os
import re
import subprocess
from pathlib import Path

import pytest

from aether import game, paths
from aether.util import AetherError

from .conftest import write_files


@pytest.fixture
def builds(repo, tmp_path, monkeypatch):
    """Fake Linux/macOS/Windows game builds, Aether's lib/ with FNA, and a built client."""
    # The Vulkan terrain extension builds (and caches) real files; not here.
    monkeypatch.setattr(game, "stage_linux_backend", lambda destination: None)
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
            "mscorlib.dll": b"mono",
            "monoconfig": "<mono/>",
            "TerrariaServer": "#!/bin/sh\n",
            "TerrariaServer.bin.x86_64": b"\x7fELF server launcher",
            "TerrariaServer.exe": b"vanilla server",
            "open-folder": "#!/bin/sh\n",
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
            "Terraria.app/Contents/MacOS/TerrariaServer": "#!/bin/bash\n",
            "Terraria.app/Contents/MacOS/osx/libFNA3D.0.dylib": b"old fna3d",
            "Terraria.app/Contents/MacOS/osx/libsteam_api.dylib": b"steam",
            "Terraria.app/Contents/Resources/Terraria.exe": b"mac exe",
            "Terraria.app/Contents/Resources/FNA.dll": b"fna 26.3",
            "Terraria.app/Contents/Resources/Content/Images/Item_1.xnb": b"mac xnb",
            "Terraria.app/Contents/Resources/mscorlib.dll": b"mono",
            "Terraria.app/Contents/Resources/steam_appid.txt": "105600",
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
    client = tmp_path / "bin" / "net10.0"
    write_files(
        client,
        {
            "Terraria.dll": b"aether client",
            "Aether.Bootstrap": b"\x7fELF apphost",
            "Aether.Bootstrap.exe": b"MZ apphost",
            "Terraria.pdb": b"pdb",
            "Terraria.runtimeconfig.json": "{}",
            "FNA.dll": b"fna 26.11",
            "FNA.dll.config": "<new/>",
            "runtimes/win/lib/net10.0/System.Drawing.Common.dll": b"drawing",
        },
    )
    return linux, mac, windows, client, repo.root / ".aether" / "run"


def assert_client(dest: Path) -> None:
    assert (dest / "Terraria.dll").read_bytes() == b"aether client"
    assert (dest / "Terraria.pdb").read_bytes() == b"pdb"
    assert (dest / "Terraria.runtimeconfig.json").is_file()
    assert (dest / "runtimes/win/lib/net10.0/System.Drawing.Common.dll").is_file()
    assert (dest / "FNA.dll").read_bytes() == b"fna 26.11"
    assert not (dest / "FNA.dll").is_symlink()


# --- Linux ------------------------------------------------------------------


def test_linux_lays_the_client_out_with_its_natives(builds):
    linux, _, _, client, run_dir = builds
    game.prepare_run_dir("linux", linux, Path(), client, run_dir)

    assert_client(run_dir)
    assert (run_dir / "Aether.Bootstrap").read_bytes() == b"\x7fELF apphost"
    assert (run_dir / "Content").is_symlink()
    assert (run_dir / "Content/Images/Item_1.xnb").read_bytes() == b"linux xnb"
    assert (run_dir / "open-folder").is_symlink()
    # Natives next to the client: Aether's FNA ones over the game's.
    assert (run_dir / "libFNA3D.so.0").read_bytes() == b"new fna3d"
    assert (run_dir / "libsteam_api.so").read_bytes() == b"steam"
    # Nothing of the game's Mono runtime.
    for name in (
        "mscorlib.dll",
        "monoconfig",
        "Terraria.bin.x86_64",
        "lib64",
        "TerrariaServer",
    ):
        assert not (run_dir / name).exists(), name


def test_linux_gives_the_vanilla_server_its_own_folder(builds):
    """Host & Play starts server/TerrariaServer: Mono, with the game's own FNA."""
    linux, _, _, client, run_dir = builds
    game.prepare_run_dir("linux", linux, Path(), client, run_dir)
    server = run_dir / "server"
    # Launchers must be real files: MonoKickstart finds lib64/ next to its real path.
    for launcher in ("TerrariaServer", "TerrariaServer.bin.x86_64"):
        assert not (server / launcher).is_symlink()
        assert (server / launcher).read_bytes() == (linux / launcher).read_bytes()
    for name, data in (
        ("TerrariaServer.exe", b"vanilla server"),
        ("FNA.dll", b"fna 26.3"),
        ("mscorlib.dll", b"mono"),
        ("lib64/libFNA3D.so.0", b"old fna3d"),
        ("Content/Images/Item_1.xnb", b"linux xnb"),
    ):
        assert (server / name).read_bytes() == data, name
    assert os.readlink(server / "Content") == os.path.join("..", "Content")
    for name in ("Terraria", "Terraria.bin.x86_64", "Terraria.exe"):
        assert not (server / name).exists()


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


def test_macos_puts_the_client_in_its_own_folder(builds, capsys):
    _, mac, _, client, run_dir = builds
    game.prepare_run_dir("macos", Path(), mac, client, run_dir)
    contents = run_dir / "Terraria.app" / "Contents"

    assert (contents / "Info.plist").read_text() == "<plist/>"
    assert not (contents / "_CodeSignature").exists()
    # The bundle's launcher starts the client in Contents/Client/.
    launcher = contents / "MacOS" / "Terraria"
    assert "../Client" in launcher.read_text()
    assert os.access(launcher, os.X_OK)
    # The vanilla server keeps the bundle's runtime (MacOS/ copied, Resources/ linked).
    macos = contents / "MacOS"
    assert not macos.is_symlink()
    assert (macos / "TerrariaServer").is_file()
    assert (macos / "osx/libFNA3D.0.dylib").read_bytes() == b"old fna3d"
    resources = contents / "Resources"
    assert resources.is_symlink()
    assert (resources / "FNA.dll").read_bytes() == b"fna 26.3"

    client_dir = contents / "Client"
    assert_client(client_dir)
    assert (client_dir / "libFNA3D.0.dylib").read_bytes() == b"new fna3d mac"
    assert (client_dir / "libSDL3.0.dylib").read_bytes() == b"new sdl3 mac"
    assert (client_dir / "libsteam_api.dylib").read_bytes() == b"steam"
    assert (client_dir / "steam_appid.txt").read_text() == "105600"
    assert (client_dir / "Content/Images/Item_1.xnb").read_bytes() == b"mac xnb"
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
        ("mac", ["mac64", "macarm"]),
        ("mac64", ["mac64"]),
        ("macarm", ["macarm"]),
        ("all", ["lin64", "win64", "mac64", "macarm"]),
    ],
)
def test_resolve_target(target, expected):
    assert game.resolve_target(target) == expected


def test_resolve_target_rejects_unknown_names():
    with pytest.raises(AetherError, match="unknown target 'win32'"):
        game.resolve_target("win32")


def outside_links(root: Path) -> list[Path]:
    """Symlinks in root that point outside it (relative ones within a package are fine)."""
    return [
        p
        for p in root.rglob("*")
        if p.is_symlink() and not p.resolve().is_relative_to(root.resolve())
    ]


@pytest.fixture
def published(builds, monkeypatch):
    """`dotnet publish` replaced by the fake client folder, recording what was published."""
    calls = []
    monkeypatch.setattr(
        game,
        "publish_client",
        lambda configuration, target: (
            calls.append((configuration, target)) or builds[3]
        ),
    )
    return calls


@pytest.mark.parametrize(
    ("target", "launcher", "client"),
    [
        ("lin64", "Aether.Bootstrap", ""),
        ("win64", "Aether.Bootstrap.exe", ""),
        (
            "mac64",
            "Terraria.app/Contents/MacOS/Terraria",
            "Terraria.app/Contents/Client",
        ),
        (
            "macarm",
            "Terraria.app/Contents/MacOS/Terraria",
            "Terraria.app/Contents/Client",
        ),
    ],
)
def test_package_copies_everything(builds, published, target, launcher, client):
    linux, mac, windows, _, _ = builds
    platform_dir = {"lin64": None, "win64": windows}.get(target, mac)
    dest = game.package(target, "Release", linux, platform_dir)

    assert published == [("Release", target)]
    assert dest == paths.DIST / target
    assert (dest / launcher).is_file()
    assert outside_links(dest) == []
    assert_client(dest / client)
    content = dest / client / "Content/Images/Item_1.xnb"
    assert content.read_bytes() == (b"mac xnb" if "mac" in target else b"linux xnb")


def test_package_replaces_the_previous_folder(builds, published):
    linux, _, _, _, _ = builds
    dest = game.package("lin64", "Release", linux, None)
    (dest / "stale.txt").write_text("left over")
    game.package("lin64", "Release", linux, None)
    assert not (dest / "stale.txt").exists()


def test_publish_command_is_self_contained_per_runtime(tmp_path):
    command = game.publish_command("Release", "osx-arm64", tmp_path)
    assert command[:2] == ["dotnet", "publish"]
    assert ["-r", "osx-arm64"] == command[command.index("-r") : command.index("-r") + 2]
    assert "--self-contained" in command
    assert {game.RUNTIMES[t] for t in game.TARGETS} == {
        "linux-x64",
        "win-x64",
        "osx-x64",
        "osx-arm64",
    }


# --- building and launching -------------------------------------------------


def test_one_anycpu_build_for_every_os():
    """`aether run` runs the framework-dependent build on any OS: nothing may pin it."""
    csproj = (paths.CLIENT / "Terraria" / "Terraria.csproj").read_text()
    assert "PlatformTarget" not in csproj
    assert "RuntimeIdentifier" not in csproj
    assert "<TargetFramework>net10.0</TargetFramework>" in csproj
    assert "PlatformTarget" not in " ".join(game.build_command("Debug"))
    assert "-nodeReuse:false" in game.build_command("Release")


@pytest.mark.parametrize(
    ("platform", "launcher"),
    [
        ("linux", "Aether.Bootstrap"),
        ("macos", "Terraria.app/Contents/MacOS/Terraria"),
        ("windows", "Aether.Bootstrap.exe"),
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


@pytest.mark.parametrize("configuration", [None, "Release"])
def test_relative_save_dir_is_resolved_against_the_callers_directory(
    repo, tmp_path, monkeypatch, configuration
):
    """The game runs inside .aether/run/, which every run wipes; saves must not land there."""
    from typer.testing import CliRunner

    from aether import cli

    seen = {}
    client = tmp_path / "client"
    monkeypatch.setattr(cli, "host_platform", lambda: "linux")
    monkeypatch.setattr(cli, "latest_build", lambda: client)
    monkeypatch.setattr(cli, "output_dir", lambda config: client / config)
    monkeypatch.setattr(cli, "check_build", lambda *args: None)
    monkeypatch.setattr(
        cli,
        "prepare_run_dir",
        lambda platform, base, other, output, run_dir: seen.update(output=output),
    )
    monkeypatch.setattr(
        cli,
        "launch",
        lambda platform, run_dir, saves, args, env=None: (
            seen.update(saves=saves, args=args) or 0
        ),
    )
    monkeypatch.chdir(tmp_path)
    options = ["--configuration", configuration] if configuration else []
    result = CliRunner().invoke(
        cli.app,
        ["launch", *options, "--save-dir", "mysaves", "--", "-world", "my world.wld"],
    )
    assert result.exit_code == 0, result.output
    assert seen == {
        "output": client / configuration if configuration else client,
        "saves": tmp_path / "mysaves",
        "args": ["-world", "my world.wld"],
    }


def test_latest_build_picks_the_newest_configuration(repo):
    debug, release = game.output_dir("Debug"), game.output_dir("Release")
    with pytest.raises(AetherError, match="aether build"):
        game.latest_build()
    for folder, mtime in ((debug, 100), (release, 200)):
        folder.mkdir(parents=True)
        for name in ("Terraria.dll", "Aether.Bootstrap.dll"):
            (folder / name).write_bytes(b"dll")
            os.utime(folder / name, (mtime, mtime))
    assert game.latest_build() == release
    os.utime(debug / "Aether.Bootstrap.dll", (300, 300))
    assert game.latest_build() == debug
    os.utime(release / "Terraria.dll", (400, 400))
    assert game.latest_build() == release
    (release / "Aether.Bootstrap.dll").unlink()
    assert game.latest_build() == debug


def test_check_build_needs_this_os_apphost(tmp_path):
    client = tmp_path / "client"
    write_files(client, {"Terraria.dll": b"dll", "Aether.Bootstrap": b"apphost"})
    game.check_build(client, "linux")
    game.check_build(client, "macos")
    with pytest.raises(AetherError, match="built on another OS"):
        game.check_build(client, "windows")
    with pytest.raises(AetherError, match="has no Terraria.dll"):
        game.check_build(tmp_path, "linux")


def test_build_command_prints_the_output(repo, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    monkeypatch.setattr(cli, "host_platform", lambda: "windows")
    built = []
    monkeypatch.setattr(
        cli,
        "build_client",
        lambda configuration, platform: (
            built.append((configuration, platform)) or game.output_dir(configuration)
        ),
    )
    result = CliRunner().invoke(cli.app, ["build", "--release"])
    assert result.exit_code == 0, result.output
    assert built == [("Release", "windows")]
    assert "bin/Release/net10.0" in result.output.replace("\\", "/")


def test_build_packages_targets(repo, tmp_path, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    linux, mac = tmp_path / "linux", tmp_path / "mac"
    monkeypatch.setattr(cli, "host_platform", lambda: "linux")
    monkeypatch.setattr(
        cli,
        "build_client",
        lambda configuration, platform: game.output_dir(configuration),
    )
    monkeypatch.setattr(cli, "default_game_dir", lambda: linux)
    monkeypatch.setattr(cli, "platform_game_dir", lambda platform: tmp_path / platform)
    packaged = []
    monkeypatch.setattr(
        cli,
        "package",
        lambda target, configuration, game_dir, platform_dir: (
            packaged.append((target, configuration, game_dir, platform_dir))
            or paths.DIST / target
        ),
    )
    result = CliRunner().invoke(
        cli.app, ["build", "--target", "macarm", "--target", "all"]
    )
    assert result.exit_code == 0, result.output
    assert packaged == [
        ("macarm", "Debug", linux, tmp_path / "macos"),
        ("lin64", "Debug", linux, None),
        ("win64", "Debug", linux, tmp_path / "windows"),
        ("mac64", "Debug", linux, tmp_path / "macos"),
    ]

    packaged.clear()
    result = CliRunner().invoke(
        cli.app, ["build", "--release", "--target", "mac", "--platform-dir", str(mac)]
    )
    assert result.exit_code == 0, result.output
    assert packaged == [
        ("mac64", "Release", linux, mac),
        ("macarm", "Release", linux, mac),
    ]


def test_build_platform_dir_needs_one_non_linux_target(repo, monkeypatch):
    from typer.testing import CliRunner

    from aether import cli

    monkeypatch.setattr(cli, "build_client", lambda *args: pytest.fail("built"))
    result = CliRunner().invoke(
        cli.app, ["build", "--target", "all", "--platform-dir", "x"]
    )
    assert result.exit_code != 0
    assert isinstance(result.exception, AetherError)


@pytest.mark.parametrize(
    ("argv", "expected"),
    [
        (["screens", "--no-build"], ["-aetherscreens"]),
        (["screens", "15", "--no-build", "--", "-x"], ["-aetherscreens", "15", "-x"]),
        (
            ["screens", "--no-build", "--control", "5000", "--", "-x"],
            ["-aetherscreens", "-aethercontrol", "5000", "-x"],
        ),
        (["screens", "--no-build", "--", "-x", "y"], ["-aetherscreens", "-x", "y"]),
    ],
)
def test_screens_opens_the_gallery(repo, tmp_path, monkeypatch, argv, expected):
    from typer.testing import CliRunner

    from aether import cli

    client = tmp_path / "client"
    seen = {}
    monkeypatch.setattr(cli, "latest_build", lambda: client)
    monkeypatch.setattr(cli, "build_client", lambda *args: pytest.fail("built"))
    monkeypatch.setattr(
        cli,
        "launch_build",
        lambda client, *rest: seen.update(client=client, args=rest[4]),
    )
    result = CliRunner().invoke(cli.app, argv)
    assert result.exit_code == 0, result.output
    assert seen == {"client": client, "args": expected}


def test_profile_env_loads_the_coreclr_profiler(monkeypatch, tmp_path):
    from aether import profiler

    library = tmp_path / "libaether-profiler.so"
    monkeypatch.setattr(profiler, "build_profiler", lambda: library)
    assert profiler.profiler_env("linux", 16) == {
        "CORECLR_ENABLE_PROFILING": "1",
        "CORECLR_PROFILER": profiler.CLSID,
        "CORECLR_PROFILER_PATH": str(library),
        "AETHER_PROFILER": "depth=16",
    }


def test_profiler_clsid_matches_the_source():
    from aether import profiler

    line = next(
        line
        for line in profiler.SOURCE.read_text().splitlines()
        if "CLSID_AetherProfiler =" in line
    )
    numbers = [int(number, 16) for number in re.findall(r"0x([0-9A-Fa-f]+)", line)]
    tail = "".join(f"{byte:02X}" for byte in numbers[3:])
    assert (
        profiler.CLSID
        == f"{{{numbers[0]:08X}-{numbers[1]:04X}-{numbers[2]:04X}-{tail[:4]}-{tail[4:]}}}"
    )


@pytest.mark.parametrize(
    ("platform", "depth", "message"),
    [
        ("windows", 16, "Linux only"),
        ("linux", 0, "between 1 and 32"),
        ("linux", 33, "between 1 and 32"),
    ],
)
def test_profile_env_rejects(monkeypatch, platform, depth, message):
    from aether import profiler
    from aether.util import AetherError

    monkeypatch.setattr(profiler, "build_profiler", lambda: pytest.fail("built"))
    with pytest.raises(AetherError, match=message):
        profiler.profiler_env(platform, depth)


def test_fna_is_built_again_when_its_patch_changes(repo, monkeypatch):
    from aether import fna

    patch = repo.root / "tools" / "fna.patch"
    patch.write_text("one")
    monkeypatch.setattr(paths, "FNA_PATCH", patch)
    monkeypatch.setattr(paths, "FNA_CACHE", repo.root / ".aether" / "fna")
    commands = []

    def run(command, cwd=None, capture_output=False):
        commands.append(command[:2])
        if command[:2] == ["git", "clone"]:
            Path(command[-1]).mkdir(parents=True)
        if command[:2] == ["dotnet", "build"]:
            write_files(
                Path(cwd) / fna.FNA_OUTPUT_DIR,
                {name: b"fna" for name in fna.FNA_OUTPUTS},
            )
        stdout = "abc\n" if command[1] == "rev-parse" else ""
        return subprocess.CompletedProcess(command, 0, stdout=stdout)

    monkeypatch.setattr(fna, "run", run)
    paths.LIB.mkdir()
    pin = {"version": "26.11", "commit": "abc"}
    fna.build_fna(pin, paths.LIB)
    assert ["git", "apply"] in commands
    assert (paths.LIB / "FNA.dll").read_bytes() == b"fna"
    commands.clear()
    fna.build_fna(pin, paths.LIB)
    assert commands == []
    patch.write_text("two")
    fna.build_fna(pin, paths.LIB)
    assert ["git", "apply"] in commands


def test_fna_patch_fits_the_pinned_fna():
    real = Path(__file__).resolve().parents[1]
    commit = json.loads((real / "tools" / "fna.json").read_text())["commit"]
    checkout = real / ".aether" / "fna" / commit
    if not (checkout / ".git").exists():
        pytest.skip("no FNA checkout (./aether setup)")
    patch = str(real / "tools" / "fna.patch")

    def fits(*flags):
        command = ["git", "apply", "--check", *flags, patch]
        result = subprocess.run(command, cwd=checkout, capture_output=True, check=False)
        return result.returncode == 0

    # Applied already, or not yet.
    assert fits("--reverse") or fits()
