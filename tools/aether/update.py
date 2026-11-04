"""Moving the base to the newest Terraria build on Steam (`aether update`).

tools/base.json pins the build the base is decompiled from; patches/GAME_VERSION records the
game version the patches are known to apply to. An update re-pins and rebuilds the base, then
re-applies the patches: if they all apply, GAME_VERSION moves to the new version; otherwise it
stays on the old one until the conflicts are fixed and `aether diff` regenerates the patches.
"""

from . import paths
from .decompile import build_base, exe_version
from .patches import (
    ensure_src_captured,
    mark_patches_compatible,
    patches_game_version,
    rebuild_src,
)
from .pinning import load_base_json, save_base_json
from .steam import (
    app_info,
    bootstrap_steamcmd,
    download_game,
    find_steamcmd,
    latest_manifests,
    platform_pin,
)
from .util import log, sha256_file


def latest_builds(cmd: list[str], pinned: dict) -> tuple[dict[str, str], str | None]:
    """The newest public manifest per platform, and Steam's public build ID."""
    app = pinned["steam"]["app"]
    depots = {"linux": pinned["steam"]["depot"]} | {
        platform: entry["depot"] for platform, entry in pinned["platforms"].items()
    }
    info = app_info(cmd, app)
    manifests = latest_manifests(info, list(depots.values()))
    buildid = (
        info.get("depots", {}).get("branches", {}).get("public", {}).get("buildid")
    )
    return {platform: manifests[depot] for platform, depot in depots.items()}, buildid


def update_available(
    cmd: list[str] | None = None,
) -> tuple[bool, dict[str, str], str | None]:
    """Whether Steam has a newer Linux build than the pinned one (no login needed)."""
    pinned = load_base_json()
    cmd = cmd or find_steamcmd() or bootstrap_steamcmd()
    latest, buildid = latest_builds(cmd, pinned)
    return latest["linux"] != pinned["steam"]["manifest"], latest, buildid


def update(username: str, force: bool = False) -> bool:
    """Re-pins and rebuilds the base on the newest build. Returns whether the patches applied."""
    cmd = find_steamcmd() or bootstrap_steamcmd()
    newer, latest, buildid = update_available(cmd)
    pinned = load_base_json()
    if not newer and not force:
        log(
            f"already on the newest build ({pinned['game_version']}, Steam build {buildid})."
        )
        return True
    if paths.STAMP.is_file():
        # Like `setup`: check for unsaved src/ edits against the old base, before replacing it.
        ensure_src_captured()

    linux_pin = {
        **platform_pin(pinned, "linux"),
        "manifest": latest["linux"],
        "exe_sha256": None,
    }
    game_dir = download_game(username, force=True, pin=linux_pin)
    exe = game_dir / "Terraria.exe"
    old_version, version = pinned["game_version"], exe_version(exe)

    pinned["game_version"] = version
    pinned["exe_sha256"] = sha256_file(exe)
    pinned["steam"]["manifest"] = latest["linux"]
    if buildid:
        pinned["steam"]["buildid"] = buildid
    for platform, entry in pinned["platforms"].items():
        if entry["manifest"] != latest[platform]:
            # Hash unknown until that build is downloaded (`aether download --platform`).
            entry["manifest"] = latest[platform]
            entry["exe_sha256"] = None
    save_base_json(pinned)
    log(f"re-pinned tools/base.json: {old_version} -> {version}.")

    build_base(game_dir, allow_mismatch=False)
    if rebuild_src(force=True):
        mark_patches_compatible(version)
        log(f"all patches apply to {version}; patches/GAME_VERSION updated.")
        return True
    log(
        f"some patches don't apply to {version}: fix the src/**/*.rej conflicts, then run "
        f"`./aether diff` (patches/ stays marked compatible with {patches_game_version()})."
    )
    return False
