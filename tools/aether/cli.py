"""The `aether` command line (typer)."""

import shutil
import subprocess
import sys
from pathlib import Path
from typing import Annotated

import typer

from . import __doc__ as package_doc
from . import paths
from .decompile import build_base
from .game import build_client, host_platform, launch, output_exe, prepare_run_dir
from .guard import stage_patches, staged_problems
from .patches import (
    digest_patch_set,
    ensure_src_captured,
    patch_files,
    rebuild_src,
    record_state,
    regenerated_patch_set,
    sync_patches,
)
from .pinning import require_base
from .steam import PLATFORMS, default_game_dir, download_game, platform_game_dir
from .tree import changed_files
from .util import AetherError, log, require_executables, run

app = typer.Typer(
    help=(package_doc or "").split("\n\n")[0] + " Edit src/, commit patches/.",
    no_args_is_help=True,
    add_completion=False,
    pretty_exceptions_enable=False,
)

ForceOption = Annotated[
    bool, typer.Option("--force", help="Discard src/ edits that aren't in patches/.")
]


@app.command()
def setup(
    game_dir: Annotated[
        Path | None,
        typer.Option(
            "--game-dir",
            envvar="TERRARIA_DIR",
            help="Terraria install dir (default: `aether download`'s copy, else the Steam library path).",
        ),
    ] = None,
    allow_version_mismatch: Annotated[
        bool,
        typer.Option(
            "--allow-version-mismatch",
            help="Continue when Terraria.exe isn't the pinned build.",
        ),
    ] = False,
    base_only: Annotated[
        bool,
        typer.Option(
            "--base-only", help="Only rebuild .base/ and lib/; leave src/ alone."
        ),
    ] = False,
    force: ForceOption = False,
) -> None:
    """Decompile the game into .base/ and lib/, install the git hooks, then build src/."""
    require_executables("git", "dotnet", "gh", "diff", "patch")
    in_git = subprocess.run(
        ["git", "rev-parse", "--is-inside-work-tree"],
        check=False,
        cwd=paths.REPO,
        capture_output=True,
    )
    if in_git.returncode == 0:
        # prek (from the uv environment) installs the hooks listed in prek.toml.
        run(["prek", "install"], cwd=paths.REPO, capture_output=True)
        if shutil.which("git-lfs"):
            run(
                ["git", "lfs", "install", "--local"],
                cwd=paths.REPO,
                capture_output=True,
            )
    else:
        log("warning: not a git checkout; skipping the git hooks.")
    run(["dotnet", "tool", "restore"], cwd=paths.REPO, capture_output=True)
    if not base_only and not force and paths.STAMP.is_file():
        # Check against the *old* base: against a rebuilt one (new recipe or game
        # version), an untouched src/ would look edited.
        ensure_src_captured()
    build_base((game_dir or default_game_dir()).expanduser(), allow_version_mismatch)
    if not base_only:
        apply(force=True)


@app.command()
def download(
    username: Annotated[
        str | None,
        typer.Option(
            "--username",
            envvar="STEAM_USERNAME",
            help="Steam account that owns Terraria (prompted if omitted).",
        ),
    ] = None,
    force: Annotated[
        bool, typer.Option("--force", help="Download again even if it's already there.")
    ] = False,
    platform: Annotated[
        str,
        typer.Option(
            "--platform", help="Which build: linux (the base), macos or windows."
        ),
    ] = "linux",
) -> None:
    """Download a pinned Terraria build with SteamCMD (the Linux base, into .aether/game/).

    `--platform macos|windows` fetches that build into .aether/game-<platform>/ instead;
    `aether run` uses it on that OS for the natives, launcher and Content it needs.
    """
    if platform not in PLATFORMS:
        raise AetherError(f"--platform must be one of {', '.join(PLATFORMS)}.")
    if not username:
        username = typer.prompt("Steam username")
    download_game(username, force=force, platform=platform)


@app.command(
    "run",
    context_settings={"allow_extra_args": True, "ignore_unknown_options": True},
)
def run_game(
    ctx: typer.Context,
    game_dir: Annotated[
        Path | None,
        typer.Option(
            "--game-dir",
            envvar="TERRARIA_DIR",
            help="Terraria install dir (default: `aether download`'s copy, else the Steam library path).",
        ),
    ] = None,
    platform_dir: Annotated[
        Path | None,
        typer.Option(
            "--platform-dir",
            help="macOS/Windows only: that OS's Terraria build, for its natives, launcher and "
            "(macOS) Content (default: `aether download --platform`'s copy, else the Steam install).",
        ),
    ] = None,
    release: Annotated[
        bool, typer.Option("--release", help="Build in Release instead of Debug.")
    ] = False,
    no_build: Annotated[
        bool, typer.Option("--no-build", help="Run the last build without rebuilding.")
    ] = False,
    save_dir: Annotated[
        Path | None,
        typer.Option(
            "--save-dir",
            help="Save directory (default: .aether/saves/, away from your real saves).",
        ),
    ] = None,
    real_saves: Annotated[
        bool, typer.Option("--real-saves", help="Use the game's normal save directory.")
    ] = False,
) -> None:
    """Build the client and run it in a scratch copy of the game (.aether/run/).

    Linux runs inside the Linux build; macOS inside a copy of the macOS build's app bundle;
    Windows with the Linux build's Content and the Windows build's DLLs. Extra arguments go to
    the game, e.g. `./aether run -- -world path/to/world.wld`. Steam has to be running.
    """
    platform = host_platform()
    configuration = "Release" if release else "Debug"
    exe = output_exe(configuration)
    if not no_build:
        exe = build_client(configuration, platform)
    elif not exe.is_file():
        raise AetherError(
            f"there's no {configuration} build yet; run without --no-build."
        )
    prepare_run_dir(
        platform,
        (game_dir or default_game_dir()).expanduser(),
        (platform_dir or platform_game_dir(platform)).expanduser()
        if platform != "linux"
        else Path(),
        exe,
        paths.RUN_DIR,
    )
    # Resolve now: the game runs with the run folder as its working directory, and that
    # folder is wiped on every run, so a relative save dir would lose the saves.
    saves = None if real_saves else (save_dir or paths.SAVES_DIR).expanduser().resolve()
    code = launch(platform, paths.RUN_DIR, saves, list(ctx.args))
    if code != 0:
        raise typer.Exit(code)


@app.command()
def diff(
    check: Annotated[
        bool,
        typer.Option(
            "--check", help="Exit 1 if patches/ is out of date; change nothing."
        ),
    ] = False,
    quiet: Annotated[
        bool, typer.Option("--quiet", help="Don't list changed patch files.")
    ] = False,
) -> None:
    """Regenerate patches/ from src/ vs .base/."""
    require_executables("diff")
    require_base()
    if not paths.SRC.is_dir():
        raise AetherError(
            "src/ doesn't exist. Run `./aether apply` (or `./aether setup`)."
        )
    new_files = regenerated_patch_set()
    if check:
        if new_files != patch_files(paths.PATCHES):
            log("patches/ is out of date with src/. Run `./aether diff`.")
            raise typer.Exit(1)
        return
    changes = sync_patches(new_files)
    record_state(digest_patch_set(new_files))
    if not quiet:
        for line in changes:
            print(line)
        print(f"{len(new_files)} patch(es); {len(changes)} change(s) to patches/.")


@app.command()
def apply(force: ForceOption = False) -> None:
    """Reset src/ to .base/ + patches/ (e.g. after pulling)."""
    require_executables("diff", "patch")
    require_base()
    if not rebuild_src(force):
        raise typer.Exit(1)


@app.command()
def status() -> None:
    """Show src/ edits vs .base/ and whether patches/ is current."""
    require_executables("diff")
    require_base()
    changes = changed_files(paths.SRC, paths.BASE)
    modified, added, deleted = changes
    for kind, files in (("M", modified), ("A", added), ("D", deleted)):
        for p in files:
            print(f"{kind} src/{p.as_posix()}")
    if not (modified or added or deleted):
        print("src/ matches .base/ (no edits).")
    up_to_date = regenerated_patch_set(changes) == patch_files(paths.PATCHES)
    print(
        "patches/ is up to date."
        if up_to_date
        else "patches/ is OUT OF DATE; run `./aether diff`."
    )


@app.command("check-staged")
def check_staged() -> None:
    """Pre-commit guard: refuse to commit game source, generated files or binaries."""
    problems = staged_problems()
    if problems:
        log("Commit refused, these staged files must not be committed:")
        for line in problems:
            log(f"  {line}")
        log("Unstage them with `git restore --staged <path>`.")
        raise typer.Exit(1)


@app.command("pre-commit")
def pre_commit() -> None:
    """Git pre-commit hook: regenerate and stage patches/, then run check-staged."""
    diff(check=False, quiet=True)
    stage_patches()
    check_staged()


def main() -> None:
    try:
        app(prog_name="aether")
    except AetherError as e:
        log(f"error: {e}")
        sys.exit(1)
    except subprocess.CalledProcessError as e:
        detail = (e.stderr or e.stdout or "").strip()
        log(
            f"error: command failed: {' '.join(map(str, e.cmd))}"
            + (f"\n{detail}" if detail else "")
        )
        sys.exit(1)
