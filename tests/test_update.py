import json

import pytest

from aether import patches, paths, steam, update
from aether.util import AetherError

from .conftest import needs_diff_patch

APP_INFO = """
"105600"
{
    "common" { "name" "Terraria" }
    "depots"
    {
        "105601" { "config" { "oslist" "windows" } "manifests" { "public" { "gid" "W2" } } }
        "105602" { "config" { "oslist" "linux" } "manifests" { "public" { "gid" "L2" "size" "1" } "v1.3.0.8" { "gid" "old" } } }
        "105603" { "manifests" { "public" { "gid" "M1" } } }
        "branches" { "public" { "buildid" "999" } }
    }
}
"""


def test_parse_keyvalues_and_latest_manifests():
    info = steam.parse_keyvalues(APP_INFO)["105600"]
    assert info["common"]["name"] == "Terraria"
    assert steam.latest_manifests(info, [105602, 105603]) == {
        105602: "L2",
        105603: "M1",
    }
    with pytest.raises(AetherError, match="no public manifest for depot 1"):
        steam.latest_manifests(info, [1])


def test_parse_keyvalues_keeps_escaped_quotes_in_one_value():
    text = r'"a" { "b" "say \"hi\"" "c" "d" }'
    assert steam.parse_keyvalues(text) == {"a": {"b": r"say \"hi\"", "c": "d"}}


@pytest.fixture
def pinned_repo(repo, monkeypatch):
    pinned = {
        **repo.pinned,
        "game_version": "1.0.0.0",
        "steam": {"app": 105600, "depot": 105602, "manifest": "L1"},
        "platforms": {
            "macos": {
                "depot": 105603,
                "manifest": "M1",
                "exe": "Terraria.app/x.exe",
                "exe_sha256": "m" * 64,
            },
            "windows": {
                "depot": 105601,
                "manifest": "W1",
                "exe": "Terraria.exe",
                "exe_sha256": "w" * 64,
            },
        },
    }
    paths.BASE_JSON.write_text(json.dumps(pinned))
    monkeypatch.setattr(update, "find_steamcmd", lambda: ["steamcmd"])
    monkeypatch.setattr(
        update, "app_info", lambda cmd, app: steam.parse_keyvalues(APP_INFO)["105600"]
    )
    return repo


def test_update_check_reports_a_newer_linux_manifest(pinned_repo):
    newer, latest, buildid = update.update_available()
    assert (
        newer
        and latest == {"linux": "L2", "macos": "M1", "windows": "W2"}
        and buildid == "999"
    )


def test_update_is_a_no_op_when_current(pinned_repo, monkeypatch):
    pinned = json.loads(paths.BASE_JSON.read_text())
    pinned["steam"]["manifest"] = "L2"
    paths.BASE_JSON.write_text(json.dumps(pinned))
    monkeypatch.setattr(
        update, "download_game", lambda *a, **k: pytest.fail("downloaded")
    )
    assert update.update("someone") is True


@needs_diff_patch
@pytest.mark.parametrize("patches_apply", [True, False])
def test_update_repins_and_tracks_patch_compatibility(
    pinned_repo, monkeypatch, tmp_path, patches_apply
):
    repo = pinned_repo
    repo.make_base({"Terraria/Main.cs": "class Main\n{\n    int a = 1;\n}\n"})
    main = repo.src / "Terraria/Main.cs"
    main.write_text(main.read_text().replace("int a = 1;", "int a = 2;"))
    repo.diff()
    patches.mark_patches_compatible("1.0.0.0")

    new_game = tmp_path / "new-game"
    new_game.mkdir()
    (new_game / "Terraria.exe").write_bytes(b"new exe")
    downloads = []

    def fake_download(username, force=False, pin=None, **kwargs):
        downloads.append(pin)
        return new_game

    def fake_build_base(game_dir, allow_mismatch):
        assert game_dir == new_game and allow_mismatch is False
        new_main = (
            "class Main\n{\n    int a = 1;\n}\n"
            if patches_apply
            else "class Main\n{\n    int a = 9;\n}\n"
        )
        (repo.base / "Terraria/Main.cs").write_text(new_main)
        pinned = json.loads(paths.BASE_JSON.read_text())
        from aether.pinning import write_stamp

        write_stamp(repo.base, pinned["exe_sha256"], pinned)

    monkeypatch.setattr(update, "download_game", fake_download)
    monkeypatch.setattr(update, "exe_version", lambda exe: "2.0.0.0")
    monkeypatch.setattr(update, "build_base", fake_build_base)

    assert update.update("someone") is patches_apply
    assert downloads[0]["manifest"] == "L2" and downloads[0]["exe_sha256"] is None

    pinned = json.loads(paths.BASE_JSON.read_text())
    assert pinned["game_version"] == "2.0.0.0"
    assert pinned["steam"]["manifest"] == "L2" and pinned["steam"]["buildid"] == "999"
    assert pinned["platforms"]["windows"] == {
        **pinned["platforms"]["windows"],
        "manifest": "W2",
        "exe_sha256": None,
    }
    assert (
        pinned["platforms"]["macos"]["exe_sha256"] == "m" * 64
    )  # unchanged manifest keeps its hash
    # Compatibility only moves to the new version once every patch applies.
    assert patches.patches_game_version() == ("2.0.0.0" if patches_apply else "1.0.0.0")


def test_download_records_a_new_platform_hash(repo, tmp_path, monkeypatch):
    pinned = {
        **repo.pinned,
        "steam": {"app": 105600, "depot": 105602, "manifest": "L"},
        "platforms": {
            "windows": {
                "depot": 105601,
                "manifest": "W2",
                "exe": "Terraria.exe",
                "exe_sha256": None,
            }
        },
    }
    paths.BASE_JSON.write_text(json.dumps(pinned))
    root = tmp_path / "root"
    root.mkdir()
    monkeypatch.setattr(steam, "steamcmd_roots", list)
    monkeypatch.setattr(steam, "find_steamcmd", lambda: [str(root / "steamcmd.exe")])

    def fake_run(cmd, check=False):
        depot = root / "steamapps/content/app_105600/depot_105601"
        depot.mkdir(parents=True)
        (depot / "Terraria.exe").write_bytes(b"windows exe")

    monkeypatch.setattr(steam.subprocess, "run", fake_run)
    steam.download_game("someone", platform="windows")
    import hashlib

    saved = json.loads(paths.BASE_JSON.read_text())["platforms"]["windows"]
    assert saved["exe_sha256"] == hashlib.sha256(b"windows exe").hexdigest()


def test_unhashed_pin_ignores_leftover_downloads(tmp_path):
    depot = tmp_path / "steamapps/content/app_105600/depot_105602"
    depot.mkdir(parents=True)
    (depot / "Terraria.exe").write_bytes(b"old download")
    pin = {"app": 105600, "depot": 105602, "exe": "Terraria.exe", "exe_sha256": None}
    future = (depot / "Terraria.exe").stat().st_mtime + 60
    assert steam.find_downloaded_depot(pin, [tmp_path], since=future) is None
    assert steam.find_downloaded_depot(pin, [tmp_path], since=0) == depot
