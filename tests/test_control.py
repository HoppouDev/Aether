import json
import socket
import threading

import pytest
from typer.testing import CliRunner

from aether import cli, control
from aether.control import Control, ControlError, GameProcess, command_line, escape_text


class FakeGame:
    """A control socket that answers like the game: one reply line per command line."""

    def __init__(self, replies):
        self.replies = replies
        self.received: list[str] = []
        self.server = socket.create_server(("127.0.0.1", 0))
        self.port = self.server.getsockname()[1]
        threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try:
                conn, _ = self.server.accept()
            except OSError:
                return
            with conn, conn.makefile("rwb") as f:
                for raw in f:
                    line = raw.decode().rstrip("\n")
                    self.received.append(line)
                    reply = self.replies(line)
                    if reply is None:
                        break
                    f.write(reply.encode() + b"\n")
                    f.flush()

    def close(self):
        self.server.close()


@pytest.fixture
def game():
    def replies(line):
        if line == "ping":
            return "ok pong"
        if line == "state":
            return "ok " + json.dumps({"screen": [960, 600], "loading": False})
        if line.startswith("bad"):
            return "error unknown command bad"
        return "ok"

    fake = FakeGame(replies)
    yield fake
    fake.close()


def test_send_returns_results_and_raises_errors(game):
    c = Control(game.port)
    assert c.send("ping") == "pong"
    assert c.state() == {"screen": [960, 600], "loading": False}
    assert c.send("mouse move 1 2 300") == ""
    with pytest.raises(ControlError, match="unknown command bad"):
        c.send("bad")
    assert game.received == ["ping", "state", "mouse move 1 2 300", "bad"]


def test_send_reconnects_once_when_the_connection_drops():
    calls = []

    def replies(line):
        calls.append(line)
        # The first connection dies without answering; the retry gets a reply.
        return None if len(calls) == 1 else "ok pong"

    fake = FakeGame(replies)
    try:
        assert Control(fake.port).send("ping") == "pong"
        assert calls == ["ping", "ping"]
    finally:
        fake.close()


def test_no_game_listening():
    with socket.create_server(("127.0.0.1", 0)) as s:
        port = s.getsockname()[1]
    c = Control(port)
    assert not c.ping()
    with pytest.raises(ControlError, match="-aethercontrol"):
        c.send("ping")


def test_command_lines():
    assert command_line("mouse move", 1, 2.5, None) == "mouse move 1 2.5"
    with pytest.raises(ControlError):
        command_line("type a\nb")
    assert escape_text("a\\b\nc\td") == "a\\\\b\\nc\\td"


def test_control_command(game):
    result = CliRunner().invoke(
        cli.app, ["control", "--port", str(game.port), "ping", "mouse click 3 4"]
    )
    assert result.exit_code == 0, result.output
    assert result.output.split() == ["pong", "ok"]
    result = CliRunner().invoke(cli.app, ["control", "--port", str(game.port), "bad"])
    assert result.exit_code == 1


def test_control_command_reads_stdin(game):
    result = CliRunner().invoke(
        cli.app, ["control", "--port", str(game.port)], input="ping\n\nwait 5\n"
    )
    assert result.exit_code == 0, result.output
    assert game.received == ["ping", "wait 5"]


def test_game_process_command(monkeypatch, tmp_path):
    seen = {}

    class Popen:
        def __init__(self, cmd, **kwargs):
            seen["cmd"] = cmd

    monkeypatch.setattr(control.subprocess, "Popen", Popen)
    GameProcess.start(5000, "screens", ["--no-build"], ["-x"], log=tmp_path / "log")
    assert seen["cmd"][3:] == [
        "screens",
        "--control",
        "5000",
        "--no-build",
        "--",
        "-x",
    ]


def test_mcp_server_tools(game):
    import asyncio

    from fastmcp import Client

    from aether.mcp_server import build_server

    async def run():
        async with Client(build_server(game.port)) as client:
            names = {t.name for t in await client.list_tools()}
            state = await client.call_tool("game_state", {})
            await client.call_tool(
                "mouse_drag", {"x1": 1, "y1": 2, "x2": 3, "y2": 4, "duration_ms": 50}
            )
            await client.call_tool("type_text", {"text": "hi\nthere"})
            return names, state.data

    names, state = asyncio.run(run())
    assert {"game_launch", "game_screenshot", "mouse_drag", "gamepad_stick"} <= names
    assert state["screen"] == [960, 600]
    assert "mouse drag 1 2 3 4 50 left" in game.received
    assert "type hi\\nthere" in game.received
