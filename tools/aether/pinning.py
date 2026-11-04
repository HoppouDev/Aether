"""The pinned game build (tools/base.json) and the stamp written into .base/."""

import json

from . import paths
from .util import AetherError, log


def load_base_json() -> dict:
    return json.loads(paths.BASE_JSON.read_text())


def save_base_json(pinned: dict) -> None:
    paths.BASE_JSON.write_text(json.dumps(pinned, indent=2) + "\n")


def write_stamp(directory, exe_sha256: str, pinned: dict) -> None:
    stamp = {
        "exe_sha256": exe_sha256,
        "recipe": pinned["recipe"],
        "ilspycmd": pinned["ilspycmd"],
    }
    (directory / paths.STAMP.name).write_text(json.dumps(stamp, indent=2) + "\n")


def require_base() -> dict:
    """Fails unless .base/ exists and was built with the current recipe."""
    if not paths.STAMP.is_file():
        raise AetherError(
            ".base/ is missing or incomplete. Run `./aether setup` first."
        )
    stamp = json.loads(paths.STAMP.read_text())
    pinned = load_base_json()
    if stamp.get("recipe") != pinned["recipe"]:
        raise AetherError(
            f".base/ was built with recipe {stamp.get('recipe')}, but tools/base.json expects "
            f"{pinned['recipe']}. Rebuild it with `./aether setup --base-only`."
        )
    if stamp.get("exe_sha256") != pinned["exe_sha256"]:
        log(
            "warning: .base/ was generated from a different Terraria.exe than the pinned one "
            f"({pinned['game_version']}); patches may not match."
        )
    return stamp
