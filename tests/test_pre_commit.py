"""The pre-commit step (`aether pre-commit`) and how prek.toml wires it up."""

import shutil
import tomllib

import pytest
from typer.testing import CliRunner

from aether import cli, guard

from .conftest import REAL_REPO, git, needs_diff_patch, needs_git, write_files

pytestmark = needs_git

runner = CliRunner()


@pytest.fixture
def git_repo(repo):
    git(repo.root, "init", "-q")
    return repo


def staged(root) -> list[str]:
    return git(root, "diff", "--cached", "--name-only").stdout.split()


def test_stage_patches_without_patches_dir(git_repo):
    guard.stage_patches()
    assert staged(git_repo.root) == []


def test_stage_patches_stages_new_patches(git_repo):
    write_files(git_repo.patches, {"Terraria/Main.cs.patch": "patch\n"})
    guard.stage_patches()
    assert staged(git_repo.root) == ["patches/Terraria/Main.cs.patch"]


def test_stage_patches_stages_removal_of_the_last_patch(git_repo):
    write_files(git_repo.patches, {"Terraria/Main.cs.patch": "patch\n"})
    git(git_repo.root, "add", "patches")
    git(git_repo.root, "commit", "-q", "-m", "chore: init")
    shutil.rmtree(git_repo.patches)  # what `aether diff` does when no edits remain

    guard.stage_patches()
    status = git(git_repo.root, "status", "--porcelain").stdout
    assert "D  patches/Terraria/Main.cs.patch" in status


@needs_diff_patch
def test_pre_commit_regenerates_and_stages_patches(git_repo):
    git_repo.make_base({"Terraria/Main.cs": "class Main\n{\n    int a = 1;\n}\n"})
    main = git_repo.src / "Terraria/Main.cs"
    main.write_text(main.read_text().replace("int a = 1;", "int a = 2;"))

    result = runner.invoke(cli.app, ["pre-commit"])
    assert result.exit_code == 0, result.output
    assert staged(git_repo.root) == [
        "patches/GAME_VERSION",
        "patches/Terraria/Main.cs.patch",
    ]
    assert (git_repo.patches / "GAME_VERSION").read_text() == "test\n"


@needs_diff_patch
def test_pre_commit_refuses_staged_game_source(git_repo):
    git_repo.make_base({"Terraria/Main.cs": "class Main\n{\n}\n"})
    git(git_repo.root, "add", "-f", "src/Terraria/Main.cs")
    assert runner.invoke(cli.app, ["pre-commit"]).exit_code == 1


def test_prek_config_runs_the_guard_first_and_skips_patches():
    config = tomllib.loads((REAL_REPO / "prek.toml").read_text())
    assert config["exclude"] == "^patches/"
    assert set(config["default_install_hook_types"]) == {"pre-commit", "commit-msg"}
    first = config["repos"][0]["hooks"][0]
    assert first["id"] == "aether"
    assert first["entry"].endswith("aether pre-commit")
    assert first["pass_filenames"] is False and first["always_run"] is True
    hooks = {h["id"]: h for r in config["repos"] for h in r["hooks"]}
    assert hooks["cocogitto"]["stages"] == ["commit-msg"]
