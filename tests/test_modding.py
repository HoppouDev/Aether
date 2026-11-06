"""Executable weaver coverage using a small, locally compiled game fixture."""

import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path
from zipfile import ZipFile

import pytest

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "tests" / "modding"
CONFIG = Path("bin/Debug/net10.0")
MOD = "Aether.Tests.Mod.dll"
DEPENDENCY = "Aether.Tests.Dependency.dll"
PRIVATE = "Aether.Tests.Private.dll"
MIXINS = ["TargetMixin", "ITargetAccess", "ITargetFactory"]


@pytest.fixture(scope="module")
def game_fixture(tmp_path_factory):
    launcher_dir = Path(
        os.environ.get("AETHER_BOOTSTRAP_TEST_DIR", ROOT / "Bootstrap" / CONFIG)
    )
    executable = "Aether.Bootstrap.exe" if os.name == "nt" else "Aether.Bootstrap"
    if not (launcher_dir / executable).is_file():
        pytest.skip(
            "build the Aether bootstrap before running executable modding tests"
        )

    for name in ("Mod", "Dependency"):
        result = subprocess.run(
            [
                "dotnet",
                "build",
                str(FIXTURES / name / f"{name}.csproj"),
                "-m:1",
                "-nodeReuse:false",
                "-p:NuGetAudit=false",
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=60,
        )
        assert result.returncode == 0, result.stdout + result.stderr

    run = tmp_path_factory.mktemp("aether-modding") / "client"
    shutil.copytree(launcher_dir, run)
    shutil.copy2(FIXTURES / "Game" / CONFIG / "Terraria.dll", run)
    return run / executable


def archive(save, name="test", files=(), extras=(), **overrides):
    mods = save / "Mods"
    mods.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schemaVersion": 1,
        "id": name,
        "version": "1.0.0",
        "gameVersion": "1.4.5.8",
        "assembly": MOD,
        **overrides,
    }
    with ZipFile(mods / f"{name}.aethermod", "w") as output:
        output.writestr("aether.mod.json", json.dumps(manifest))
        for file in files:
            output.write(file, file.name)
        for path, contents in extras:
            output.writestr(path, contents)


def run(game, save, *args):
    return subprocess.run(
        [str(game), "-savedirectory", str(save), *args],
        check=False,
        capture_output=True,
        text=True,
        timeout=30,
    )


def gameplay_archive(save, mixins=MIXINS, **manifest):
    mappings = {
        "schemaVersion": 1,
        "selectors": {"LegacyTarget": "Target", "oldValue": "value"},
    }
    archive(
        save,
        files=[
            FIXTURES / "Mod" / CONFIG / MOD,
            FIXTURES / "Private" / CONFIG / PRIVATE,
        ],
        extras=[("mappings.json", json.dumps(mappings))],
        mixins=mixins,
        entrypoint="Init",
        mappings="mappings.json",
        **manifest,
    )


def test_woven_code_executes_with_dependencies(game_fixture, tmp_path):
    archive(
        tmp_path,
        "z-dependency",
        files=[FIXTURES / "Dependency" / CONFIG / DEPENDENCY],
        assembly=DEPENDENCY,
        entrypoint="DependencyInit",
    )
    gameplay_archive(tmp_path, depends=["z-dependency"])
    pristine = game_fixture.parent / "Terraria.dll"
    before = hashlib.sha256(pristine.read_bytes()).digest()

    result = run(game_fixture, tmp_path, "-mixindebug")
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == ["DEPENDENCY", "FINALLY", "MOD OK", "GAME"]
    assert "locals: 0=System.Int32" in result.stderr
    assert hashlib.sha256(pristine.read_bytes()).digest() == before


def test_no_mods_bypasses_invalid_packages(game_fixture, tmp_path):
    archive(tmp_path, extras=[("../escape", "unsafe")])
    result = run(game_fixture, tmp_path, "-nomods")
    assert result.returncode == 0, result.stderr
    assert result.stdout.strip() == "GAME"

    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert "Unsafe archive path" in result.stderr
    assert not (tmp_path / "escape").exists()
    assert "test.aethermod" in (tmp_path / "Logs/mod-load-error.log").read_text()


@pytest.mark.parametrize(
    ("overrides", "diagnostic"),
    [
        ({"gameVersion": "1.0.0.0"}, "requires game"),
        ({"depends": ["absent"]}, "requires missing mod"),
        ({"depends": ["test"]}, "Dependency cycle"),
    ],
)
def test_invalid_dependency_metadata(game_fixture, tmp_path, overrides, diagnostic):
    gameplay_archive(tmp_path, **overrides)
    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert diagnostic in result.stderr
    assert not result.stdout


@pytest.mark.parametrize(
    ("mixins", "diagnostic"),
    [
        (["InvalidMixin"], "missing injection target 'Missing'"),
        ([*MIXINS, "ConflictingMixin"], "conflicts with mod 'test'"),
    ],
)
def test_invalid_mixin_stops_before_initializers(
    game_fixture, tmp_path, mixins, diagnostic
):
    gameplay_archive(tmp_path, mixins=mixins)
    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert diagnostic in result.stderr
    assert not result.stdout


def test_duplicate_ids_fail_before_loading_assemblies(game_fixture, tmp_path):
    entry = FIXTURES / "Mod" / CONFIG / MOD
    archive(tmp_path, files=[entry])
    archive(tmp_path, "second", files=[entry], id="test")
    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert "Duplicate mod ID 'test'" in result.stderr
    assert not result.stdout
