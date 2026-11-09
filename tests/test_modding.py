"""Executable weaver coverage using a small, locally compiled game fixture."""

import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
from pathlib import Path
from zipfile import ZipFile

import pytest

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "tests" / "modding"
CONFIG = Path("bin/Debug/net10.0")
MOD = "Aether.Tests.Mod.dll"
DEPENDENCY = "Aether.Tests.Dependency.dll"
PRIVATE = "Aether.Tests.Private.dll"
API_LIBRARY = "Aether.Tests.ApiLibrary.dll"
MIXINS = ["TargetMixin", "ITargetAccess", "ITargetFactory"]


@pytest.fixture(scope="module")
def game_fixture(tmp_path_factory):
    launcher_dir = Path(
        os.environ.get(
            "AETHER_BOOTSTRAP_TEST_DIR", ROOT / "client" / "Bootstrap" / CONFIG
        )
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


def archive(save, archive_name="test", files=(), extras=(), **overrides):
    mods = save / "Mods"
    mods.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schemaVersion": 1,
        "id": archive_name,
        "version": "1.0.0",
        "depends": {"aether": ">=0.1.0", "terraria": "1.4.5.8"},
        "assembly": MOD,
        **overrides,
    }
    with ZipFile(mods / f"{archive_name}.aethermod", "w") as output:
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


def connection_profile(game, save, *args):
    result = run(game, save, "-testconnection", *args)
    assert result.returncode == 0, result.stderr
    profile, end = result.stdout.splitlines()
    assert end == "GAME"
    return profile


def test_connection_profile_uses_exact_archives(game_fixture, tmp_path):
    assembly = FIXTURES / "Mod" / CONFIG / MOD
    dependency = FIXTURES / "Dependency" / CONFIG / DEPENDENCY
    empty = connection_profile(game_fixture, tmp_path, "-aetherjoin")
    assert empty.startswith("Aether/2/")
    assert connection_profile(game_fixture, tmp_path) == "VANILLA"

    archive(tmp_path, "alpha", [assembly], extras=[("resources/data.txt", "first")])
    archive(tmp_path, "beta", [dependency], assembly=DEPENDENCY)
    profile = connection_profile(game_fixture, tmp_path, "-aetherjoin")
    assert profile != empty
    assert connection_profile(game_fixture, tmp_path) == "VANILLA"
    assert connection_profile(game_fixture, tmp_path, "-aetherjoin", "-nomods") == empty

    mods = tmp_path / "Mods"
    (mods / "alpha.aethermod").rename(mods / "zz-alpha.aethermod")
    assert connection_profile(game_fixture, tmp_path, "-aetherjoin") == profile

    (mods / "zz-alpha.aethermod").unlink()
    archive(tmp_path, "alpha", [assembly], extras=[("resources/data.txt", "changed")])
    assert connection_profile(game_fixture, tmp_path, "-aetherjoin") != profile


@pytest.fixture(scope="module")
def api_assemblies(game_fixture, tmp_path_factory):
    output = tmp_path_factory.mktemp("aether-api")
    for name in ("FirstConsumer", "SecondConsumer"):
        build = output / name
        result = subprocess.run(
            [
                "dotnet",
                "build",
                str(FIXTURES / "ApiConsumer" / "ApiConsumer.csproj"),
                "-m:1",
                "-nodeReuse:false",
                "-p:NuGetAudit=false",
                f"-p:ConsumerName={name}",
                "-o",
                str(build),
            ],
            check=False,
            capture_output=True,
            text=True,
            timeout=60,
        )
        assert result.returncode == 0, result.stdout + result.stderr
        shutil.copy2(build / f"{name}.dll", output)
    shutil.copy2(build / "Aether.Api.dll", output)
    shutil.copy2(build / API_LIBRARY, output)
    return output


def api_archive(save, assemblies):
    archive(
        save,
        "aether-api",
        files=[assemblies / "Aether.Api.dll", assemblies / API_LIBRARY],
        assembly="Aether.Api.dll",
        version="0.1.0",
    )


def test_api_and_its_libraries_are_shared_with_consumers_and_woven_game(
    game_fixture, api_assemblies, tmp_path
):
    api_archive(tmp_path, api_assemblies)
    for name, mixins in [("FirstConsumer", ["ApiHook"]), ("SecondConsumer", [])]:
        archive(
            tmp_path,
            name.lower(),
            files=[api_assemblies / f"{name}.dll", api_assemblies / API_LIBRARY],
            assembly=f"{name}.dll",
            entrypoint="ApiConsumer",
            mixins=mixins,
            depends={"aether-api": "0.1.0"},
        )

    result = run(game_fixture, tmp_path, "-testapi")
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == ["1", "2", "3", "GAME"]


@pytest.mark.parametrize(
    ("install_api", "constraint", "bundle", "diagnostic"),
    [
        (False, "0.1.0", False, "requires missing mod 'aether-api'"),
        (True, "0.2.0", False, "requires 'aether-api' 0.2.0; found 0.1.0"),
        (True, None, False, "references require a declared dependency"),
        (True, "0.1.0", True, "must be installed as a separate package"),
    ],
)
def test_rejects_invalid_api_dependencies(
    game_fixture, api_assemblies, tmp_path, install_api, constraint, bundle, diagnostic
):
    if install_api:
        api_archive(tmp_path, api_assemblies)
    files = [api_assemblies / "FirstConsumer.dll"]
    if bundle:
        files.append(api_assemblies / "Aether.Api.dll")
    archive(
        tmp_path,
        "consumer",
        files=files,
        assembly="FirstConsumer.dll",
        entrypoint="ApiConsumer",
        depends={"aether-api": constraint} if constraint else {},
    )

    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert diagnostic in result.stderr
    assert not result.stdout


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
        extras=[
            ("mappings.json", json.dumps(mappings)),
            ("resources/nested/value.txt", "50"),
        ],
        mixins=mixins,
        entrypoint="Init",
        mappings="mappings.json",
        **manifest,
    )


@pytest.mark.parametrize("dependency_mixins", [[], ["DependencyMixin"]])
def test_woven_code_executes_with_dependencies(
    game_fixture, tmp_path, dependency_mixins
):
    archive(
        tmp_path,
        "z-dependency",
        files=[
            FIXTURES / "Dependency" / CONFIG / DEPENDENCY,
            FIXTURES / "Private" / CONFIG / PRIVATE,
        ],
        assembly=DEPENDENCY,
        entrypoint="DependencyInit",
        mixins=dependency_mixins,
    )
    gameplay_archive(
        tmp_path,
        depends={
            "aether": "~0.1",
            "terraria": ">=1.4.5.8 <1.4.5.9",
            "z-dependency": ["~0.9", ">=1.0.0 <2"],
        },
    )
    pristine = game_fixture.parent / "Terraria.dll"
    before = hashlib.sha256(pristine.read_bytes()).digest()

    result = run(game_fixture, tmp_path, "-mixindebug")
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == ["DEPENDENCY", "FINALLY", "MOD OK", "GAME"]
    assert "locals: 0=System.Int32" in result.stderr
    assert hashlib.sha256(pristine.read_bytes()).digest() == before


@pytest.mark.skipif(sys.platform != "linux", reason="Linux client native lookup")
def test_woven_game_loads_native_library_from_client_directory(game_fixture, tmp_path):
    compiler = shutil.which("cc")
    if not compiler:
        pytest.skip("a C compiler is required for the native fixture")
    client = tmp_path / "client"
    shutil.copytree(game_fixture.parent, client)
    source = tmp_path / "fixture.c"
    source.write_text("int FixtureValue(void) { return 42; }\n")
    subprocess.run(
        [
            compiler,
            "-shared",
            "-fPIC",
            str(source),
            "-o",
            str(client / "libaether_fixture_native.so"),
        ],
        check=True,
        timeout=15,
    )
    gameplay_archive(tmp_path)
    result = run(client / game_fixture.name, tmp_path, "-testnative")
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines()[0] == "NATIVE 42"


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


@pytest.mark.parametrize("entry", ["aether.mod.json", "resources/oversized.txt"])
def test_rejects_zip_entries_larger_than_declared(game_fixture, tmp_path, entry):
    archive(
        tmp_path,
        files=[FIXTURES / "Mod" / CONFIG / MOD],
        extras=[("resources/oversized.txt", b"x" * 1024)],
    )
    package = tmp_path / "Mods/test.aethermod"
    image = bytearray(package.read_bytes())
    end = image.rindex(b"PK\x05\x06")
    offset = struct.unpack_from("<I", image, end + 16)[0]
    while image[offset : offset + 4] == b"PK\x01\x02":
        name_length, extra_length, comment_length = struct.unpack_from(
            "<HHH", image, offset + 28
        )
        name = image[offset + 46 : offset + 46 + name_length]
        if name == entry.encode():
            # Leave the compressed length and Stored payload intact.
            struct.pack_into("<I", image, offset + 24, 1)
            break
        offset += 46 + name_length + extra_length + comment_length
    else:
        pytest.fail(f"Fixture entry missing: {entry}")
    package.write_bytes(image)

    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert f"Archive entry '{entry}' exceeds its declared size." in result.stderr


@pytest.mark.parametrize(
    ("overrides", "diagnostic"),
    [
        (
            {"depends": {"terraria": "1.0.0.0"}},
            "requires 'terraria' 1.0.0.0; found 1.4.5.8",
        ),
        ({"depends": {"aether": ">=99"}}, "requires 'aether' >=99; found"),
        ({"depends": {"absent": "*"}}, "requires missing mod"),
        ({"depends": {"test": "*"}}, "Dependency cycle"),
        (
            {"depends": {"aether": ">=1..0"}},
            "Invalid 'depends' constraint for 'aether'",
        ),
        ({"depends": {"aether": []}}, "Expected at least one version constraint"),
        ({"depends": ["test"]}, "'depends' must be an object"),
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


@pytest.mark.parametrize("license_value", [None, "MIT", ["MIT", "Apache-2.0"]])
def test_mod_catalog_exposes_display_metadata(game_fixture, tmp_path, license_value):
    fields = {}
    extras = [("resources/icon.png", b"\x89PNG\r\n\x1a\n")]
    if license_value is not None:
        fields = {
            "name": "Metadata Example",
            "description": "A fixture mod",
            "authors": ["Alex", "Sam"],
            "contributors": ["Taylor"],
            "source": "https://example.org/inventory-tools/source",
            "homepage": "https://example.org/inventory-tools",
            "license": license_value,
        }
        extras = [("icon.png", b"\x89PNG\r\n\x1a\n")]

    archive(
        tmp_path,
        "metadata",
        files=[FIXTURES / "Dependency" / CONFIG / DEPENDENCY],
        extras=extras,
        assembly=DEPENDENCY,
        entrypoint="MetadataInit",
        **fields,
    )
    result = run(game_fixture, tmp_path)
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == ["METADATA OK", "GAME"]


@pytest.mark.parametrize("field", ["authors", "contributors"])
def test_rejects_person_objects(game_fixture, tmp_path, field):
    archive(
        tmp_path,
        "metadata",
        files=[FIXTURES / "Dependency" / CONFIG / DEPENDENCY],
        assembly=DEPENDENCY,
        entrypoint="MetadataInit",
        **{field: [{"name": "Alex"}]},
    )
    result = run(game_fixture, tmp_path)
    assert result.returncode != 0
    assert f"'{field}' must contain nonempty text" in result.stderr
    assert not result.stdout


@pytest.mark.parametrize(
    ("installed", "constraint", "matches"),
    [
        ("1.2.3-beta.10+build", ">1.2.3-beta.2 <1.2.3", True),
        ("1.2.3-rc.1", ">=1.2.3", False),
        ("1.2.3.4", "1.2.3.x", True),
        ("1.2", "=1.2.0.0", True),
        ("2.0", "^1.5", False),
        ("1.3", "~1.2", False),
        ("release-name", "*", True),
    ],
)
def test_mod_dependency_version_constraints(
    game_fixture, tmp_path, installed, constraint, matches
):
    archive(
        tmp_path,
        "z-dependency",
        files=[FIXTURES / "Dependency" / CONFIG / DEPENDENCY],
        assembly=DEPENDENCY,
        entrypoint="DependencyInit",
        version=installed,
    )
    gameplay_archive(tmp_path, depends={"z-dependency": constraint})
    result = run(game_fixture, tmp_path)
    if matches:
        assert result.returncode == 0, result.stderr
        assert result.stdout.splitlines() == ["DEPENDENCY", "FINALLY", "MOD OK", "GAME"]
    else:
        assert result.returncode != 0
        assert (
            f"requires 'z-dependency' {constraint}; found {installed}" in result.stderr
        )
        assert not result.stdout
