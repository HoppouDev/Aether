import json

import pytest

from aether import paths, pinning
from aether.util import AetherError

from .conftest import REAL_REPO


def test_require_base_without_stamp(repo):
    with pytest.raises(AetherError, match="aether setup"):
        pinning.require_base()


def test_require_base_accepts_matching_stamp(repo, capsys):
    paths.BASE.mkdir()
    pinning.write_stamp(paths.BASE, repo.pinned["exe_sha256"], repo.pinned)
    assert pinning.require_base()["recipe"] == repo.pinned["recipe"]
    assert capsys.readouterr().err == ""


def test_require_base_rejects_other_recipe(repo):
    paths.BASE.mkdir()
    pinning.write_stamp(
        paths.BASE, repo.pinned["exe_sha256"], {**repo.pinned, "recipe": 99}
    )
    with pytest.raises(AetherError, match="recipe 99"):
        pinning.require_base()


def test_require_base_warns_on_other_exe(repo, capsys):
    paths.BASE.mkdir()
    pinning.write_stamp(paths.BASE, "f" * 64, repo.pinned)
    pinning.require_base()
    assert "different Terraria.exe" in capsys.readouterr().err


def test_real_base_json_is_well_formed():
    real = json.loads((REAL_REPO / "tools" / "base.json").read_text())
    assert set(real) >= {"game_version", "exe_sha256", "ilspycmd", "recipe"}
    assert len(real["exe_sha256"]) == 64
