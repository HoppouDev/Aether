"""Driving a running client through its MCP server (`-aethermcp [PORT]`).

The client itself serves the `aether-game` MCP server on http://127.0.0.1:PORT/mcp (stateless
Streamable HTTP; the tools are in client/Terraria/Aether/Testing/GameTools.cs). `aether control`
runs raw control commands (client/Terraria/Aether/Testing/ControlServer.cs) through its
`game_send` tool. Input goes straight into the game: no window focus, cursor movement or system
input is involved.
"""

import json
import urllib.error
import urllib.request
from dataclasses import dataclass

from .util import AetherError

DEFAULT_PORT = 47321


class ControlError(AetherError):
    """The game rejected a command, or couldn't be reached."""


@dataclass
class Control:
    """A client's MCP server."""

    port: int = DEFAULT_PORT
    host: str = "127.0.0.1"
    timeout: float = 120.0

    def call(self, tool: str, arguments: dict) -> list[dict]:
        """Calls one of the server's tools and returns its content blocks."""
        body = json.dumps(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "tools/call",
                "params": {"name": tool, "arguments": arguments},
            }
        ).encode()
        request = urllib.request.Request(
            f"http://{self.host}:{self.port}/mcp",
            body,
            {
                "Content-Type": "application/json",
                "Accept": "application/json, text/event-stream",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                text = response.read().decode()
        except TimeoutError as e:
            raise ControlError(f"timed out waiting for {tool}") from e
        except urllib.error.URLError as e:
            raise ControlError(
                f"no game serving MCP on {self.host}:{self.port} ({e.reason}); "
                "start it with --mcp"
            ) from e
        # A stateless server answers with one server-sent event (or plain JSON).
        events = [
            line[5:].strip() for line in text.splitlines() if line.startswith("data:")
        ]
        message = json.loads(events[-1] if events else text)
        if "error" in message:
            raise ControlError(message["error"].get("message", "MCP error"))
        result = message["result"]
        content = result.get("content", [])
        if result.get("isError"):
            raise ControlError(" ".join(block.get("text", "") for block in content))
        return content

    def send(self, line: str) -> str:
        """Runs one control command and returns its result (the text after `ok`)."""
        if "\n" in line or "\r" in line:
            raise ControlError(f"a command can't contain a line break: {line!r}")
        reply = self.call("game_send", {"commands": [line]})[0]["text"]
        status = reply[len(line) + len(" -> ") :]
        if status.startswith("error "):
            raise ControlError(status[len("error ") :])
        return status[len("ok") :].strip()
