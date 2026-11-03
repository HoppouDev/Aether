from pathlib import Path

import pytest

from aether.tree import changed_files, is_binary, is_tracked_path, tree_files

from .conftest import write_files


@pytest.mark.parametrize(
    ("rel", "tracked"),
    [
        ("Terraria/Main.cs", True),
        ("Resources/x.json", True),
        ("Terraria/bin.cs", True),  # only *directories* named bin/obj are skipped
        ("AGENTS.md", False),
        ("Terraria/UI/AGENTS.md", False),
        ("Terraria/Main.cs.rej", False),
        ("Terraria/Main.cs.orig", False),
        ("bin/Debug/x.dll", False),
        ("Terraria/obj/x.cs", False),
    ],
)
def test_is_tracked_path(rel, tracked):
    assert is_tracked_path(Path(rel)) is tracked


def test_tree_files_skips_stamp_docs_and_build_output(tmp_path):
    write_files(
        tmp_path,
        {
            "a.cs": "a",
            "dir/b.cs": "b",
            ".stamp": "{}",
            "dir/AGENTS.md": "docs",
            "obj/x.cs": "x",
            "dir/c.cs.rej": "r",
        },
    )
    assert tree_files(tmp_path) == {Path("a.cs"), Path("dir/b.cs")}


def test_tree_files_of_missing_dir_is_empty(tmp_path):
    assert tree_files(tmp_path / "nope") == set()


def test_changed_files(tmp_path):
    src, base = tmp_path / "src", tmp_path / "base"
    write_files(base, {"same.cs": "1", "mod.cs": "old", "gone.cs": "x"})
    write_files(src, {"same.cs": "1", "mod.cs": "new", "new/added.cs": "y"})
    assert changed_files(src, base) == (
        [Path("mod.cs")],
        [Path("new/added.cs")],
        [Path("gone.cs")],
    )


def test_is_binary(tmp_path):
    write_files(tmp_path, {"t.json": '{"a": 1}\n', "b.dll": b"MZ\x00\x01"})
    assert not is_binary(tmp_path / "t.json")
    assert is_binary(tmp_path / "b.dll")
