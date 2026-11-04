import sys

import pytest
from typer.testing import CliRunner

from aether import cli

from .conftest import needs_diff_patch

pytestmark = needs_diff_patch

runner = CliRunner()
BASE_FILES = {"Terraria/Main.cs": "class Main\n{\n    int a = 1;\n}\n"}


def test_diff_check_and_status(repo):
    repo.make_base(BASE_FILES)
    # No patches/GAME_VERSION yet: patches aren't marked compatible with the base.
    assert runner.invoke(cli.app, ["diff", "--check"]).exit_code == 1
    assert (
        "marked patches/ compatible with test"
        in runner.invoke(cli.app, ["diff"]).output
    )
    assert runner.invoke(cli.app, ["diff", "--check"]).exit_code == 0
    assert (
        "base and patches/: Terraria test." in runner.invoke(cli.app, ["status"]).output
    )

    main = repo.src / "Terraria/Main.cs"
    main.write_text(main.read_text().replace("int a = 1;", "int a = 2;"))
    assert runner.invoke(cli.app, ["diff", "--check"]).exit_code == 1

    result = runner.invoke(cli.app, ["status"])
    assert result.exit_code == 0
    assert "M src/Terraria/Main.cs" in result.output
    assert "OUT OF DATE" in result.output

    result = runner.invoke(cli.app, ["diff"])
    assert result.exit_code == 0
    assert "created patches/Terraria/Main.cs.patch" in result.output
    assert runner.invoke(cli.app, ["diff", "--check"]).exit_code == 0
    assert "patches/ is up to date." in runner.invoke(cli.app, ["status"]).output


def test_apply_exit_code_reflects_conflicts(repo):
    repo.make_base(BASE_FILES)
    main = repo.src / "Terraria/Main.cs"
    main.write_text(main.read_text().replace("int a = 1;", "int a = 2;"))
    assert runner.invoke(cli.app, ["diff"]).exit_code == 0
    assert runner.invoke(cli.app, ["apply"]).exit_code == 0

    base_main = repo.base / "Terraria/Main.cs"
    base_main.write_text(base_main.read_text().replace("int a = 1;", "int a = 3;"))
    assert runner.invoke(cli.app, ["apply", "--force"]).exit_code == 1


def test_main_reports_errors_without_traceback(repo, monkeypatch, capsys):
    monkeypatch.setattr(sys, "argv", ["aether", "status"])  # no .base/ yet
    with pytest.raises(SystemExit) as exit_info:
        cli.main()
    assert exit_info.value.code == 1
    err = capsys.readouterr().err
    assert err.startswith("error: .base/ is missing")
    assert "Traceback" not in err


def test_missing_tools_are_reported(repo, monkeypatch):
    monkeypatch.setattr("aether.util.shutil.which", lambda name: None)
    result = runner.invoke(cli.app, ["diff"])
    assert "required tool(s) not found on PATH: diff" in str(result.exception)


@pytest.fixture
def stub_setup(repo, monkeypatch):
    """Stubs out decompiling, dotnet and git hooks; build_base adds a file to .base/."""
    calls = []

    def fake_build_base(game_dir, allow_mismatch):
        calls.append("build_base")
        (repo.base / "ReLogic").mkdir(exist_ok=True)
        (repo.base / "ReLogic/New.cs").write_text("class New\n{\n}\n")

    monkeypatch.setattr(cli, "build_base", fake_build_base)
    monkeypatch.setattr(cli, "run", lambda *a, **k: None)
    monkeypatch.setattr(cli, "require_executables", lambda *names: None)
    return calls


def test_setup_survives_a_rebuilt_base(repo, stub_setup):
    """An untouched src/ must not look 'edited' just because the new base has more files."""
    repo.make_base(BASE_FILES)
    result = runner.invoke(cli.app, ["setup"])
    assert result.exit_code == 0, result.output
    assert stub_setup == ["build_base"]
    assert (repo.src / "ReLogic/New.cs").is_file()


def test_setup_refuses_uncaptured_edits_before_rebuilding(repo, stub_setup):
    repo.make_base(BASE_FILES)
    main = repo.src / "Terraria/Main.cs"
    main.write_text(main.read_text().replace("int a = 1;", "int a = 2;"))

    result = runner.invoke(cli.app, ["setup"])
    assert "would be lost" in str(result.exception)
    assert stub_setup == []  # .base/ was left alone
    assert "int a = 2;" in main.read_text()

    assert runner.invoke(cli.app, ["setup", "--force"]).exit_code == 0
    assert "int a = 1;" in main.read_text()
