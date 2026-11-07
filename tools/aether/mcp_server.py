"""The `aether-game` MCP server: tools for agents to play and test the client (`aether mcp`).

It drives the game through its control socket (control.py), so input never goes through the
window system: the window needn't be focused or visible, and the real cursor never moves.
"""

import json
import tempfile
import time
from pathlib import Path
from typing import Annotated, Literal

from fastmcp import FastMCP
from fastmcp.utilities.types import Image
from pydantic import Field

from .control import (
    DEFAULT_PORT,
    Control,
    ControlError,
    GameProcess,
    command_line,
    escape_text,
)

INSTRUCTIONS = """\
Drives a running Aether (Terraria) client with synthetic mouse, keyboard and gamepad input
that bypasses the window system: the window needn't have focus and the real cursor never moves.
Start with game_launch (or connect to a client already started with -aethercontrol).
Use game_ui_layout and game_ui_action for direct interaction with retained controls.
Use game_screenshot and the mouse_/key_/type_/gamepad_ tools for other areas.
Coordinates are window pixels, the same as a full-size screenshot's (game_state gives the
screen size; scale a scaled screenshot's coordinates back up). Every tool returns once its
input has finished (a 400 ms drag returns after 400 ms). game_send runs raw control commands
(see src/Aether/Testing/ControlServer.cs) in one call, for sequences."""

Button = Literal["left", "right", "middle"]
Ms = Annotated[
    float, Field(ge=0, description="Duration in milliseconds (0 = instant).")
]


class Session:
    def __init__(self, port: int) -> None:
        self.control = Control(port)
        self.game: GameProcess | None = None
        self.shots = Path(tempfile.mkdtemp(prefix="aether-shots-"))
        self.count = 0

    def send(self, *words: object) -> str:
        return self.control.send(command_line(*words))

    def shot(
        self, scale: float = 0.5, crop: list[int] | None = None
    ) -> tuple[Path, str]:
        self.count += 1
        path = self.shots / f"shot-{self.count:04}.png"
        words: list[object] = ["screenshot"]
        if crop:
            words += ["crop", *crop]
        words += ["scale", scale, path]
        return path, self.send(*words)


def build_server(port: int = DEFAULT_PORT) -> FastMCP:
    session = Session(port)
    server = FastMCP("aether-game", instructions=INSTRUCTIONS)

    @server.tool()
    def game_launch(
        build: Annotated[
            bool, Field(description="Build the client first (takes ~1 minute).")
        ] = True,
        screens: Annotated[
            bool,
            Field(
                description="Start with -aetherscreens: the title-menu screen gallery and the "
                "in-game test fixture keys (see src/Aether/AGENTS.md)."
            ),
        ] = False,
        save_dir: Annotated[
            str | None,
            Field(description="Save directory (default .aether/saves/)."),
        ] = None,
        game_args: Annotated[
            list[str] | None, Field(description="Extra game launch arguments.")
        ] = None,
        profile: Annotated[
            bool,
            Field(
                description="Preload the allocation profiler (Linux), for game_profile. "
                "Allocation is slower for the whole run."
            ),
        ] = False,
        timeout: Annotated[float, Field(description="Seconds to wait for it.")] = 300,
    ) -> str:
        """Start the client with its control socket on, and wait until it answers.

        Stops a client this server started before. Steam must be running.
        """
        if session.control.ping() and (
            session.game is None or not session.game.running()
        ):
            raise ControlError(
                f"a client not started by this server already listens on port {port}; "
                "use it, or game_quit it first"
            )
        if session.game is not None:
            session.game.stop()
            session.control.close()
        options: list[str] = []
        if screens:
            command = "screens"
            if not build:
                options.append("--no-build")
        else:
            command = "run" if build else "launch"
        if save_dir:
            options += ["--save-dir", str(Path(save_dir).expanduser().resolve())]
        if profile:
            options.append("--profile")
        session.game = GameProcess.start(port, command, options, game_args)
        session.game.wait_ready(session.control, timeout)
        state = session.control.state()
        return (
            f"running (pid {session.game.process.pid}, port {port}, log {session.game.log}); "
            f"screen {state['screen'][0]}x{state['screen'][1]}"
        )

    @server.tool()
    def game_quit() -> str:
        """Close the client (unsaved progress is lost), whether or not this server started it."""
        if session.game is not None and session.game.running():
            code = session.game.stop()
            session.control.close()
            return f"stopped (exit code {code})"
        if not session.control.ping():
            return "no client is listening"
        session.send("quit")
        session.control.close()
        return "asked the client to quit"

    @server.tool()
    def game_state() -> dict:
        """Screen size, title-menu mode, synthetic input, and in game the player's state."""
        return session.control.state()

    @server.tool()
    def game_ui_layout(interactive_only: bool = False) -> dict:
        """Inspect the next rendered UI frame. Components include IDs, labels, bounds and available actions.

        Set interactive_only to return only actionable controls. IDs belong to the latest layout.
        """
        return json.loads(
            session.send("ui layout", "interactive" if interactive_only else None)
        )

    @server.tool()
    def game_ui_action(
        component_id: Annotated[str, Field(pattern=r"^[0-9a-f]{8}-[1-9][0-9]*$")],
        action: Literal["activate", "secondary", "set_value", "set_text"] = "activate",
        value: str | float | None = None,
    ) -> str:
        """Act on a component from game_ui_layout without moving the cursor.

        Use a number from 0 to 1 for set_value and a string for set_text. Refresh after UI changes.
        """
        return (
            session.send(
                "ui act",
                component_id,
                action,
                json.dumps(value) if value is not None else None,
            )
            or "ok"
        )

    @server.tool()
    def game_screenshot(
        scale: Annotated[
            float,
            Field(
                gt=0,
                le=1,
                description="Downscale factor; multiply coordinates read off the image by "
                "1/scale (relative to the crop's origin) before clicking.",
            ),
        ] = 0.5,
        crop: Annotated[
            list[int] | None,
            Field(
                min_length=4,
                max_length=4,
                description="[x, y, width, height] in window pixels, to look closely.",
            ),
        ] = None,
    ) -> list:
        """Capture the next drawn frame (the game's own back buffer, not the screen)."""
        path, result = session.shot(scale, crop)
        return [Image(data=path.read_bytes(), format="png"), result.split(" ")[0]]

    @server.tool()
    def mouse_move(x: float, y: float, duration_ms: Ms = 0) -> str:
        """Move the synthetic cursor to (x, y), gliding (eased) over duration_ms."""
        return session.send("mouse move", x, y, duration_ms) or "ok"

    @server.tool()
    def mouse_click(
        x: float | None = None,
        y: float | None = None,
        button: Button = "left",
        hold_frames: Annotated[int, Field(ge=1)] = 2,
        count: Annotated[int, Field(ge=1, le=10, description="Clicks in a row.")] = 1,
    ) -> str:
        """Click (at (x, y), else where the cursor is), holding the button hold_frames updates."""
        for i in range(count):
            at = (x, y) if x is not None and y is not None and i == 0 else ()
            session.send("mouse click", button, *at, hold_frames)
        return "ok"

    @server.tool()
    def mouse_drag(
        x1: float,
        y1: float,
        x2: float,
        y2: float,
        duration_ms: Ms = 400,
        button: Button = "left",
    ) -> str:
        """Press at (x1, y1), glide to (x2, y2) over duration_ms, release."""
        return session.send("mouse drag", x1, y1, x2, y2, duration_ms, button) or "ok"

    @server.tool()
    def mouse_button(button: Button, down: bool) -> str:
        """Press (down=true) or release a mouse button and leave it there (for long holds)."""
        return session.send("mouse", "down" if down else "up", button) or "ok"

    @server.tool()
    def mouse_wheel(notches: int) -> str:
        """Scroll the wheel; positive is up (away from you)."""
        return session.send("mouse wheel", notches) or "ok"

    @server.tool()
    def key_press(
        keys: Annotated[
            str,
            Field(
                description="XNA key names joined by '+', e.g. 'Escape', 'ctrl+shift+l', "
                "'E', 'D1', 'Space'. Aliases: ctrl, shift, alt, esc, enter, backspace, 0-9."
            ),
        ],
        hold_frames: Annotated[int, Field(ge=1)] = 2,
        hold_ms: Annotated[
            float | None,
            Field(ge=0, description="Hold for this long instead (e.g. to walk)."),
        ] = None,
    ) -> str:
        """Press a key or chord and release it."""
        if hold_ms is None:
            return session.send("key press", keys, hold_frames) or "ok"
        session.send("key down", keys)
        try:
            session.send("wait", hold_ms)
        finally:
            session.send("key up", keys)
        return "ok"

    @server.tool()
    def key_hold(keys: str, down: bool) -> str:
        """Hold keys down (down=true) or release them; they stay until released."""
        return session.send("key", "down" if down else "up", keys) or "ok"

    @server.tool()
    def type_text(text: str) -> str:
        """Type text into the focused text field (a newline is Enter)."""
        return session.send("type", escape_text(text)) or "ok"

    @server.tool()
    def gamepad_connect(connected: bool = True) -> str:
        """Plug in (or unplug) the synthetic gamepad; it replaces a real first gamepad."""
        return session.send("pad", "connect" if connected else "disconnect") or "ok"

    @server.tool()
    def gamepad_button(
        buttons: Annotated[
            str,
            Field(
                description="XNA Buttons joined by '+': A, B, X, Y, Start, Back, "
                "LeftShoulder/lb, RightShoulder/rb, LeftStick/ls, RightStick/rs, "
                "DPadUp/up, DPadDown/down, DPadLeft/left, DPadRight/right, lt, rt."
            ),
        ],
        action: Literal["press", "down", "up"] = "press",
        hold_frames: Annotated[int, Field(ge=1)] = 2,
    ) -> str:
        """Press, hold down or release gamepad buttons (connects the pad if needed)."""
        connect()
        return session.send("pad button", buttons, action, hold_frames) or "ok"

    @server.tool()
    def gamepad_stick(
        stick: Literal["left", "right"],
        x: Annotated[float, Field(ge=-1, le=1)],
        y: Annotated[float, Field(ge=-1, le=1, description="Up is positive.")],
        duration_ms: Ms = 150,
        hold_ms: Annotated[
            float | None,
            Field(
                ge=0,
                description="Hold there this long, then glide back to centre over "
                "duration_ms; omit to leave the stick there.",
            ),
        ] = None,
    ) -> str:
        """Move a thumbstick smoothly to (x, y) (connects the pad if needed)."""
        connect()
        session.send("pad stick", stick, x, y, duration_ms)
        if hold_ms is not None:
            session.send("wait", hold_ms)
            session.send("pad stick", stick, 0, 0, duration_ms)
        return "ok"

    @server.tool()
    def gamepad_trigger(
        trigger: Literal["left", "right"],
        value: Annotated[float, Field(ge=0, le=1)],
        duration_ms: Ms = 100,
        hold_ms: Annotated[
            float | None,
            Field(ge=0, description="Hold this long, then release; omit to leave it."),
        ] = None,
    ) -> str:
        """Pull a trigger smoothly to value (connects the pad if needed)."""
        connect()
        session.send("pad trigger", trigger, value, duration_ms)
        if hold_ms is not None:
            session.send("wait", hold_ms)
            session.send("pad trigger", trigger, 0, duration_ms)
        return "ok"

    @server.tool()
    def game_profile(
        frames: Annotated[
            int, Field(ge=1, le=3600, description="Updates to record over.")
        ] = 120,
        top: Annotated[
            int, Field(ge=1, le=500, description="Sites and methods to list.")
        ] = 40,
    ) -> str:
        """Record every managed allocation's class, size and call stack for a number of updates
        (needs game_launch with profile: true), and return the report, per frame: the methods
        with the most bytes allocated under them, then the allocation sites by bytes."""
        session.count += 1
        path = session.shots / f"profile-{session.count:04}.txt"
        session.send("profile", frames, top, path)
        return path.read_text()

    @server.tool()
    def game_wait(
        ms: Annotated[float | None, Field(ge=0)] = None,
        frames: Annotated[int | None, Field(ge=1)] = None,
    ) -> str:
        """Let the game run for ms milliseconds or a number of updates."""
        if frames is not None:
            return session.send("wait frames", frames) or "ok"
        return session.send("wait", ms if ms is not None else 0) or "ok"

    @server.tool()
    def release_all() -> str:
        """Release every synthetic button, key, stick and trigger."""
        return session.send("release") or "ok"

    @server.tool()
    def game_send(
        commands: Annotated[
            list[str],
            Field(
                description="Control commands, one per item, e.g. ['key press e', "
                "'mouse drag 100 200 400 200 500', 'wait 250']."
            ),
        ],
        screenshot_scale: Annotated[
            float | None,
            Field(gt=0, le=1, description="Also screenshot at the end, at this scale."),
        ] = None,
    ) -> list:
        """Run raw control commands in order; returns each result (stops at an error)."""
        results: list = []
        started = time.monotonic()
        for line in commands:
            try:
                results.append(f"{line} -> ok {session.send(line)}".rstrip())
            except ControlError as e:
                results.append(f"{line} -> error {e}")
                break
        results.append(f"{time.monotonic() - started:.2f}s")
        if screenshot_scale is not None:
            path, _ = session.shot(screenshot_scale)
            results.append(Image(data=path.read_bytes(), format="png"))
        return results

    def connect() -> None:
        if not session.control.state().get("pad"):
            session.send("pad connect")

    return server


def serve(port: int = DEFAULT_PORT) -> None:
    build_server(port).run("stdio", show_banner=False)
