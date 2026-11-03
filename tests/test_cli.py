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
    assert runner.invoke(cli.app, ["diff", "--check"]).exit_code == 0

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
