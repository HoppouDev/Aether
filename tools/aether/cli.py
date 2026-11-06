"""The `aether` command line (typer)."""

import shutil
import subprocess
import sys
from pathlib import Path
from typing import Annotated

import typer

from . import __doc__ as package_doc
from . import paths
from .control import DEFAULT_PORT, Control, ControlError
from .decompile import build_base
from .game import (
    TARGETS,
    build_client,
    check_build,
    host_platform,
    latest_build,
    launch,
    package,
    prepare_run_dir,
    resolve_target,
)
from .guard import stage_patches, staged_problems
from .patches import (
    digest_patch_set,
    ensure_src_captured,
    mark_patches_compatible,
    patch_files,
    patches_game_version,
    rebuild_src,
    record_state,
    regenerated_patch_set,
    sync_patches,
)
from .pinning import load_base_json, require_base
from .profiler import profiler_env
from .steam import PLATFORMS, default_game_dir, download_game, platform_game_dir
from .tree import changed_files
from .update import update as update_base
from .update import update_available
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


@app.command()
def update(
    username: Annotated[
        str | None,
        typer.Option(
            "--username",
            envvar="STEAM_USERNAME",
            help="Steam account that owns Terraria (prompted if omitted).",
        ),
    ] = None,
    check: Annotated[
        bool,
        typer.Option(
            "--check",
            help="Only check for a newer build (exit 1 if there is one); no login needed.",
        ),
    ] = False,
    force: Annotated[
        bool,
        typer.Option("--force", help="Rebuild even if already on the newest build."),
    ] = False,
) -> None:
    """Move the base to the newest Terraria build on Steam and re-apply the patches.

    Downloads the new Linux build, re-pins tools/base.json, rebuilds .base/ and src/, and
    marks patches/ compatible with the new version if every patch applies (exit 1 if not:
    fix the .rej conflicts, then `./aether diff`).
    """
    if check:
        newer, latest, buildid = update_available()
        pinned = load_base_json()
        if newer:
            print(
                f"newer build on Steam (build {buildid}, Linux manifest {latest['linux']}); "
                f"pinned: {pinned['game_version']} (manifest {pinned['steam']['manifest']})."
            )
            raise typer.Exit(1)
        print(f"up to date: {pinned['game_version']} (Steam build {buildid}).")
        return
    require_executables("git", "dotnet", "gh", "diff", "patch")
    if not username:
        username = typer.prompt("Steam username")
    if not update_base(username, force):
        raise typer.Exit(1)


GameDirOption = Annotated[
    Path | None,
    typer.Option(
        "--game-dir",
        envvar="TERRARIA_DIR",
        help="Terraria install dir (default: `aether download`'s copy, else the Steam library path).",
    ),
]
PlatformDirOption = Annotated[
    Path | None,
    typer.Option(
        "--platform-dir",
        help="macOS/Windows only: that OS's Terraria build, for its natives, launcher and "
        "(macOS) Content (default: `aether download --platform`'s copy, else the Steam install).",
    ),
]
ReleaseOption = Annotated[
    bool, typer.Option("--release", help="Build in Release instead of Debug.")
]
SaveDirOption = Annotated[
    Path | None,
    typer.Option(
        "--save-dir",
        help="Save directory (default: .aether/saves/, away from your real saves).",
    ),
]
RealSavesOption = Annotated[
    bool, typer.Option("--real-saves", help="Use the game's normal save directory.")
]
ControlOption = Annotated[
    int | None,
    typer.Option(
        "--control",
        help="Accept input and commands on this local port (-aethercontrol): `aether control`, "
        f"the `aether mcp` server (default port {DEFAULT_PORT}).",
    ),
]
ProfileOption = Annotated[
    bool,
    typer.Option(
        "--profile",
        help="Preload Aether's allocation profiler (Linux): the control command `profile` "
        "(MCP: game_profile) then records allocation call stacks. Use with --control.",
    ),
]
ProfileDepthOption = Annotated[
    int,
    typer.Option("--profile-depth", help="Stack frames kept per allocation (1-32)."),
]
GAME_ARGS = {"allow_extra_args": True, "ignore_unknown_options": True}


def game_args(ctx: typer.Context, control: int | None) -> list[str]:
    return [*(["-aethercontrol", str(control)] if control else []), *ctx.args]


def launch_build(
    client: Path,
    game_dir: Path | None,
    platform_dir: Path | None,
    save_dir: Path | None,
    real_saves: bool,
    args: list[str],
    profile_depth: int | None = None,
) -> None:
    """Runs a built client in a scratch copy of the game; exits with the game's exit code.

    profile_depth: preload the allocation profiler, keeping that many frames per stack.
    """
    platform = host_platform()
    check_build(client, platform)
    env = profiler_env(platform, profile_depth) if profile_depth is not None else None
    log(
        "launching "
        + str(
            client.relative_to(paths.REPO)
            if client.is_relative_to(paths.REPO)
            else client
        )
    )
    prepare_run_dir(
        platform,
        (game_dir or default_game_dir()).expanduser(),
        (platform_dir or platform_game_dir(platform)).expanduser()
        if platform != "linux"
        else Path(),
        client,
        paths.RUN_DIR,
    )
    # Resolve now: the game runs with the run folder as its working directory, and that
    # folder is wiped on every run, so a relative save dir would lose the saves.
    saves = None if real_saves else (save_dir or paths.SAVES_DIR).expanduser().resolve()
    code = launch(platform, paths.RUN_DIR, saves, args, env)
    if code != 0:
        raise typer.Exit(code)


@app.command()
def build(
    release: ReleaseOption = False,
    target: Annotated[
        list[str] | None,
        typer.Option(
            "--target",
            help="Also package a self-contained dist/<target>/ to play on that OS: lin64, "
            "win64, mac64 (Intel), macarm (Apple silicon), mac (both) or all. Repeatable.",
        ),
    ] = None,
    game_dir: GameDirOption = None,
    platform_dir: PlatformDirOption = None,
) -> None:
    """Build the client (Terraria.csproj with ReLogic), and optionally package it per OS.

    The client is a .NET 10 app; a target folder publishes it self-contained for that OS
    (with the .NET runtime) and lays it out with the game's files, from the Linux build (the
    base) and, for mac, the macOS build (`./aether download --platform macos`); win64 also takes
    TerrariaServer.exe from the Windows build if it's there. dist/ holds game files: it's for
    playing, not sharing.
    """
    targets = list(
        dict.fromkeys(t for name in target or [] for t in resolve_target(name))
    )
    platforms = {TARGETS[t] for t in targets if TARGETS[t] != "linux"}
    if platform_dir is not None and len(platforms) != 1:
        raise AetherError(
            "--platform-dir needs exactly one OS among the mac and win64 targets (it's that OS's build)."
        )
    configuration = "Release" if release else "Debug"
    client = build_client(configuration, host_platform())
    print(client.relative_to(paths.REPO))
    base = (game_dir or default_game_dir()).expanduser()
    for name in targets:
        platform = TARGETS[name]
        other = (
            None
            if platform == "linux"
            else (platform_dir or platform_game_dir(platform))
        )
        print(
            package(
                name, configuration, base, other and other.expanduser()
            ).relative_to(paths.REPO)
        )


@app.command("launch", context_settings=GAME_ARGS)
def launch_command(
    ctx: typer.Context,
    game_dir: GameDirOption = None,
    platform_dir: PlatformDirOption = None,
    save_dir: SaveDirOption = None,
    real_saves: RealSavesOption = False,
    control: ControlOption = None,
    profile: ProfileOption = False,
    profile_depth: ProfileDepthOption = 16,
) -> None:
    """Run the most recent build (Debug or Release) without building.

    Same run folder and options as `aether run`; extra arguments go to the game.
    """
    launch_build(
        latest_build(),
        game_dir,
        platform_dir,
        save_dir,
        real_saves,
        game_args(ctx, control),
        profile_depth if profile else None,
    )


@app.command("run", context_settings=GAME_ARGS)
def run_game(
    ctx: typer.Context,
    game_dir: GameDirOption = None,
    platform_dir: PlatformDirOption = None,
    release: ReleaseOption = False,
    save_dir: SaveDirOption = None,
    real_saves: RealSavesOption = False,
    control: ControlOption = None,
    profile: ProfileOption = False,
    profile_depth: ProfileDepthOption = 16,
) -> None:
    """Build the client and run it in a scratch copy of the game (.aether/run/).

    Linux runs inside the Linux build; macOS inside a copy of the macOS build's app bundle;
    Windows with the Linux build's Content and the Windows build's DLLs. Extra arguments go to
    the game, e.g. `./aether run -- -world path/to/world.wld`. Steam has to be running.
    `aether build` and `aether launch` do the two halves separately.
    """
    client = build_client("Release" if release else "Debug", host_platform())
    launch_build(
        client,
        game_dir,
        platform_dir,
        save_dir,
        real_saves,
        game_args(ctx, control),
        profile_depth if profile else None,
    )


@app.command("screens", context_settings=GAME_ARGS)
def screens(
    ctx: typer.Context,
    mode: Annotated[
        str | None,
        typer.Argument(
            help="A menuMode to open directly (default: the gallery of all migrated screens).",
            show_default=False,
        ),
    ] = None,
    game_dir: GameDirOption = None,
    platform_dir: PlatformDirOption = None,
    release: ReleaseOption = False,
    no_build: Annotated[
        bool, typer.Option("--no-build", help="Use the most recent build as is.")
    ] = False,
    save_dir: SaveDirOption = None,
    real_saves: RealSavesOption = False,
    control: ControlOption = None,
    profile: ProfileOption = False,
    profile_depth: ProfileDepthOption = 16,
) -> None:
    """Build the client and open its title-menu screen gallery (`-aetherscreens`).

    Each migrated retained-mode screen can be previewed on its own, with sample state and inert
    clicks; Esc goes back to the gallery and Tab switches to the legacy version of the screen.
    """
    client = (
        latest_build()
        if no_build
        else build_client("Release" if release else "Debug", host_platform())
    )
    extra = game_args(ctx, control)
    # After `--`, click hands the first game argument to MODE when there's no mode.
    if mode is not None and not mode.isdigit():
        if not mode.startswith("-"):
            raise typer.BadParameter(f"{mode!r} isn't a menuMode", param_hint="MODE")
        extra.insert(len(extra) - len(ctx.args), mode)
        mode = None
    args = ["-aetherscreens", *([mode] if mode is not None else []), *extra]
    launch_build(
        client,
        game_dir,
        platform_dir,
        save_dir,
        real_saves,
        args,
        profile_depth if profile else None,
    )


PortOption = Annotated[
    int,
    typer.Option("--port", help="The game's control port (its -aethercontrol PORT)."),
]


@app.command()
def control(
    commands: Annotated[
        list[str] | None,
        typer.Argument(
            help="Commands, one per argument (e.g. 'mouse move 400 300 250'); "
            "default: one per line from stdin."
        ),
    ] = None,
    port: PortOption = DEFAULT_PORT,
) -> None:
    """Send commands to a client started with `-aethercontrol` and print their results.

    The input goes straight into the game (no window focus or system input): mouse moves and
    drags, keys, typing, a synthetic gamepad, screenshots and state. Start a client for it with
    e.g. `./aether run --control 47321`. The commands are listed in
    src/Aether/Testing/ControlServer.cs; agents can use the `aether mcp` server instead.
    """
    game = Control(port)
    lines = commands if commands else (line.rstrip("\n") for line in sys.stdin)
    for line in lines:
        if not line.strip():
            continue
        try:
            result = game.send(line)
        except ControlError as e:
            log(f"error: {line}: {e}")
            raise typer.Exit(1) from e
        print(result if result else "ok")


@app.command()
def mcp(port: PortOption = DEFAULT_PORT) -> None:
    """Run the `aether-game` MCP server (stdio), which lets agents launch and drive the client.

    Registered for this repo in .mcp.json; see .agents/skills/aether-game/SKILL.md.
    """
    from .mcp_server import serve

    serve(port)


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
    base_version = load_base_json()["game_version"]
    if check:
        if new_files != patch_files(paths.PATCHES):
            log("patches/ is out of date with src/. Run `./aether diff`.")
            raise typer.Exit(1)
        if patches_game_version() != base_version:
            log(
                f"patches/ isn't marked compatible with {base_version}. Run `./aether diff`."
            )
            raise typer.Exit(1)
        return
    changes = sync_patches(new_files)
    record_state(digest_patch_set(new_files))
    # Patches generated against the base apply to the base's game version by definition.
    if mark_patches_compatible(base_version):
        changes.append(f"marked patches/ compatible with {base_version}")
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
    mark_patches_compatible(load_base_json()["game_version"])


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
    base_version, compatible = load_base_json()["game_version"], patches_game_version()
    if compatible == base_version:
        print(f"base and patches/: Terraria {base_version}.")
    else:
        print(
            f"base is Terraria {base_version}, but patches/ is marked compatible with "
            f"{compatible or 'no version'}: fix any .rej conflicts, then `./aether diff`."
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
