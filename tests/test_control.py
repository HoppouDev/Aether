import json
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest
from typer.testing import CliRunner

from aether import cli
from aether.control import Control, ControlError


class FakeGame:
    """An MCP server that answers game_send like the client: one result line per command."""

    def __init__(self, replies):
        self.replies = replies
        self.received: list[str] = []
        game = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                request = json.loads(
                    self.rfile.read(int(self.headers["Content-Length"]))
                )
                params = request["params"]
                assert request["method"] == "tools/call"
                assert params["name"] == "game_send"
                content = []
                for line in params["arguments"]["commands"]:
                    game.received.append(line)
                    content.append(
                        {
                            "type": "text",
                            "text": f"{line} -> {game.replies(line)}".rstrip(),
                        }
                    )
                body = {
                    "jsonrpc": "2.0",
                    "id": request["id"],
                    "result": {"content": content},
                }
                data = f"event: message\ndata: {json.dumps(body)}\n\n".encode()
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.port = self.server.server_address[1]
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()


@pytest.fixture
def game():
    def replies(line):
        if line == "ping":
            return "ok pong"
        if line.startswith("bad"):
            return "error unknown command bad"
        return "ok"

    fake = FakeGame(replies)
    yield fake
    fake.close()


def test_send_returns_results_and_raises_errors(game):
    c = Control(game.port)
    assert c.send("ping") == "pong"
    assert c.send("mouse move 1 2 300") == ""
    with pytest.raises(ControlError, match="unknown command bad"):
        c.send("bad")
    with pytest.raises(ControlError, match="line break"):
        c.send("type a\nb")
    assert game.received == ["ping", "mouse move 1 2 300", "bad"]


def test_no_game_serving():
    with socket.create_server(("127.0.0.1", 0)) as s:
        port = s.getsockname()[1]
    with pytest.raises(ControlError, match="--mcp"):
        Control(port).send("ping")


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
