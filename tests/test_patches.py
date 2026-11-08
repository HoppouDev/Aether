import json
import shutil

import pytest

from aether import patches
from aether.util import AetherError

from .conftest import needs_diff_patch, read_tree, write_files

pytestmark = needs_diff_patch

BASE_FILES = {
    "Terraria/Main.cs": "class Main\n{\n    int a = 1;\n    int b = 2;\n    int c = 3;\n}\n",
    "Terraria/Old.cs": "class Old\n{\n}\n",
    "Resources/Lang.json": '{\n  "Iron": "Iron Pickaxe",\n}\n',
}


def edit(path, old, new):
    text = path.read_text()
    assert old in text
    path.write_text(text.replace(old, new))


def test_unchanged_src_produces_no_patches(repo):
    repo.make_base(BASE_FILES)
    assert repo.diff() == []
    assert not repo.patches.exists()


def test_modify_add_delete_round_trip(repo):
    repo.make_base(BASE_FILES)
    edit(repo.src / "Terraria/Main.cs", "int b = 2;", "int b = 42;")
    write_files(repo.src, {"Terraria/Aether/New.cs": "class New\n{\n}\n"})
    (repo.src / "Terraria/Old.cs").unlink()
    expected = read_tree(repo.src)

    changes = repo.diff()
    assert sorted(changes) == [
        "created patches/Terraria/Aether/New.cs.patch",
        "created patches/Terraria/Main.cs.patch",
        "created patches/Terraria/Old.cs.patch",
    ]
    main_patch = (repo.patches / "Terraria/Main.cs.patch").read_text()
    assert main_patch.startswith("--- a/Terraria/Main.cs\n+++ b/Terraria/Main.cs\n")
    assert "-    int b = 2;\n+    int b = 42;\n" in main_patch
    assert (
        (repo.patches / "Terraria/Aether/New.cs.patch")
        .read_text()
        .startswith("--- /dev/null\n+++ b/Terraria/Aether/New.cs\n")
    )
    assert (
        (repo.patches / "Terraria/Old.cs.patch")
        .read_text()
        .startswith("--- a/Terraria/Old.cs\n+++ /dev/null\n")
    )

    shutil.rmtree(repo.src)
    assert patches.rebuild_src(force=False)
    assert read_tree(repo.src) == expected


def test_patches_keep_utf8_bytes_exactly(repo):
    repo.make_base(BASE_FILES)
    edit(repo.src / "Resources/Lang.json", "Iron Pickaxe", "鉄のつるはし")
    repo.diff()
    assert (
        "鉄のつるはし".encode()
        in (repo.patches / "Resources/Lang.json.patch").read_bytes()
    )
    shutil.rmtree(repo.src)
    assert patches.rebuild_src(force=False)
    assert "鉄のつるはし" in (repo.src / "Resources/Lang.json").read_text(
        encoding="utf-8"
    )


def test_crlf_only_change_produces_no_patch(repo):
    repo.make_base(BASE_FILES)
    path = repo.src / "Terraria/Main.cs"
    path.write_bytes(path.read_bytes().replace(b"\n", b"\r\n"))
    assert repo.diff() == []


def test_crlf_edit_produces_a_minimal_lf_patch(repo):
    repo.make_base(BASE_FILES)
    path = repo.src / "Terraria/Main.cs"
    path.write_bytes(
        path.read_bytes().replace(b"int b = 2;", b"int b = 9;").replace(b"\n", b"\r\n")
    )
    repo.diff()
    patch = (repo.patches / "Terraria/Main.cs.patch").read_bytes()
    assert b"\r" not in patch
    assert (
        patch.count(b"\n-") == 1 and patch.count(b"\n+") == 2
    )  # +++ header and one added line


def test_binary_change_is_rejected(repo):
    repo.make_base({**BASE_FILES, "Resources/blob.bin": b"\x00\x01"})
    (repo.src / "Resources/blob.bin").write_bytes(b"\x00\x02")
    with pytest.raises(AetherError, match="binary files"):
        patches.regenerated_patch_set()


def test_sync_removes_stale_patches_and_empty_dirs(repo):
    repo.make_base(BASE_FILES)
    write_files(repo.src, {"Terraria/Deep/Dir/X.cs": "x\n"})
    repo.diff()
    (repo.src / "Terraria/Deep/Dir/X.cs").unlink()
    assert repo.diff() == ["removed patches/Terraria/Deep/Dir/X.cs.patch"]
    assert not (repo.patches / "Terraria").exists()


def test_rebuild_refuses_to_discard_uncaptured_edits(repo):
    repo.make_base(BASE_FILES)
    edit(repo.src / "Terraria/Main.cs", "int a = 1;", "int a = 7;")
    repo.diff()
    edit(repo.src / "Terraria/Main.cs", "int c = 3;", "int c = 8;")  # not captured
    with pytest.raises(AetherError, match="would be lost"):
        patches.rebuild_src(force=False)
    assert "int c = 8;" in (repo.src / "Terraria/Main.cs").read_text()

    assert patches.rebuild_src(force=True)
    text = (repo.src / "Terraria/Main.cs").read_text()
    assert "int a = 7;" in text and "int c = 3;" in text


def test_rebuild_after_patches_change_upstream_needs_no_force(repo):
    """src/ untouched since the last sync and patches/ replaced (e.g. by `git pull`)."""
    repo.make_base(BASE_FILES)
    repo.diff()
    write_files(
        repo.patches,
        {
            "Terraria/Main.cs.patch": (
                "--- a/Terraria/Main.cs\n+++ b/Terraria/Main.cs\n"
                "@@ -1,3 +1,3 @@\n class Main\n {\n-    int a = 1;\n+    int a = 5;\n"
            )
        },
    )
    assert patches.rebuild_src(force=False)
    assert "int a = 5;" in (repo.src / "Terraria/Main.cs").read_text()


def test_rebuild_keeps_agents_md_and_clears_leftovers(repo):
    repo.make_base(BASE_FILES)
    write_files(
        repo.src, {"Terraria/AGENTS.md": "docs", "Terraria/Main.cs.rej": "old reject"}
    )
    assert patches.rebuild_src(force=True)
    assert (repo.src / "Terraria/AGENTS.md").read_text() == "docs"
    assert not (repo.src / "Terraria/Main.cs.rej").exists()


def test_conflicting_patch_leaves_a_reject(repo, capsys):
    repo.make_base(BASE_FILES)
    edit(repo.src / "Terraria/Main.cs", "int b = 2;", "int b = 42;")
    repo.diff()
    edit(
        repo.base / "Terraria/Main.cs", "int b = 2;", "int b = 100;"
    )  # e.g. a game update

    assert patches.rebuild_src(force=True) is False
    assert (repo.src / "Terraria/Main.cs.rej").is_file()
    assert "FAILED Terraria/Main.cs" in capsys.readouterr().err


def test_offset_patch_still_applies(repo, capsys):
    repo.make_base(BASE_FILES)
    edit(repo.src / "Terraria/Main.cs", "int c = 3;", "int c = 30;")
    repo.diff()
    edit(
        repo.base / "Terraria/Main.cs",
        "class Main\n",
        "// header\n// header\nclass Main\n",
    )

    assert patches.rebuild_src(force=True)
    assert "int c = 30;" in (repo.src / "Terraria/Main.cs").read_text()
    assert "offset/fuzz" in capsys.readouterr().err


def test_build_applies_patches_that_changed_since_the_last_sync(repo):
    from aether import game

    repo.make_base({"A.cs": "one\n"})
    (repo.src / "A.cs").write_text("two\n")
    repo.diff()
    assert not patches.patches_changed_since_sync()
    # A pull brings a newer patch; src/ still has the old edit.
    pulled = (repo.patches / "A.cs.patch").read_text().replace("+two", "+three")
    (repo.patches / "A.cs.patch").write_text(pulled)
    assert patches.patches_changed_since_sync()
    game.update_src()
    assert (repo.src / "A.cs").read_text() == "three\n"
    assert not patches.patches_changed_since_sync()


def test_build_keeps_unsynced_src_edits(repo):
    from aether import game

    repo.make_base({"A.cs": "one\n"})
    (repo.src / "A.cs").write_text("edited\n")
    game.update_src()
    assert (repo.src / "A.cs").read_text() == "edited\n"


def test_state_without_a_patches_digest_falls_back_to_the_src_digest(repo):
    repo.make_base({"A.cs": "one\n"})
    (repo.src / "A.cs").write_text("two\n")
    repo.diff()
    digest = patches.recorded_digest()
    patches.state_file().write_text(json.dumps({"src_patch_digest": digest}))
    assert not patches.patches_changed_since_sync()
    (repo.patches / "A.cs.patch").write_text("changed")
    assert patches.patches_changed_since_sync()
