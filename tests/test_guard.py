import pytest

from aether import guard

from .conftest import git, needs_git, write_files

pytestmark = needs_git

GAME_FILE = "class Main\n{\n    int secret = 1;\n}\n"


@pytest.fixture
def staged(repo):
    """Stages files in a fresh git repo at the fixture root and returns staged_problems()."""
    git(repo.root, "init", "-q")
    write_files(repo.base, {"Terraria/Main.cs": GAME_FILE})

    def stage(files: dict[str, str | bytes]) -> list[str]:
        write_files(repo.root, files)
        git(repo.root, "add", "-f", "--", *files)
        return guard.staged_problems()

    return stage


def test_project_files_and_patches_are_allowed(staged):
    assert (
        staged(
            {
                "tools/aether/x.py": "print()\n",
                "patches/Terraria/Main.cs.patch": "--- a/Terraria/Main.cs\n+++ b/Terraria/Main.cs\n",
                "src/Terraria/AGENTS.md": "docs\n",
                "client/Terraria/Terraria.csproj": "<Project />\n",
            }
        )
        == []
    )


@pytest.mark.parametrize(
    ("path", "reason"),
    [
        ("src/Terraria/Main.cs", "game source"),
        ("src/Resources/Lang.json", "game source"),
        (".base/Terraria/Main.cs", "generated"),
        ("lib/ReLogic.dll", "generated"),
        (".aether/state.json", "generated"),
        ("assets/Item_1.xnb", "binary/game asset"),
        ("tools/helper.dll", "binary/game asset"),
    ],
)
def test_forbidden_paths_are_refused(staged, path, reason):
    problems = staged({path: "x\n"})
    assert len(problems) == 1
    assert problems[0].startswith(f"{path}: ") and reason in problems[0]


def test_verbatim_copy_of_a_game_file_is_refused(staged):
    problems = staged({"tools/Main.cs": GAME_FILE})
    assert problems == ["tools/Main.cs: identical to a decompiled game file"]


def test_modified_copy_is_not_flagged_as_verbatim(staged):
    assert staged({"tools/Main.cs": GAME_FILE + "// mine\n"}) == []
