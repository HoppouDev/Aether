"""Driving a running client through its control socket (`-aethercontrol [PORT]`).

The game listens on 127.0.0.1 and runs one command per line on its main thread, feeding the
input to the game in place of the real devices: no window focus, cursor movement or system
input is involved. Each command is answered with one line once it has finished (`ok [RESULT]`
or `error MESSAGE`). The protocol is documented in src/Aether/Testing/ControlServer.cs.
"""

import json
import os
import signal
import socket
import subprocess
import sys
import threading
import time
from dataclasses import dataclass, field
from io import BufferedRWPair
from pathlib import Path

from . import paths
from .util import AetherError

DEFAULT_PORT = 47321
LOG = paths.STATE / "control.log"


class ControlError(AetherError):
    """The game rejected a command, or couldn't be reached."""


def command_line(*words: object) -> str:
    """Joins words into one command line; refuses line breaks, which would split it."""
    line = " ".join(
        str(int(w)) if isinstance(w, float) and w.is_integer() else str(w)
        for w in words
        if w is not None
    )
    if "\n" in line or "\r" in line:
        raise ControlError(f"a command can't contain a line break: {line!r}")
    return line


def escape_text(text: str) -> str:
    """Escapes text for `type`: backslashes, and line breaks/tabs as \\n and \\t."""
    return (
        text.replace("\\", "\\\\")
        .replace("\r\n", "\n")
        .replace("\n", "\\n")
        .replace("\r", "\\n")
        .replace("\t", "\\t")
    )


@dataclass
class Control:
    """A connection to the game's control socket (opened on first use, reopened if it drops)."""

    port: int = DEFAULT_PORT
    host: str = "127.0.0.1"
    timeout: float = 120.0
    _sock: socket.socket | None = field(default=None, repr=False)
    _file: BufferedRWPair | None = field(default=None, repr=False)
    _lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    def _connect(self) -> BufferedRWPair:
        try:
            sock = socket.create_connection((self.host, self.port), timeout=5)
        except OSError as e:
            raise ControlError(
                f"no game listening on {self.host}:{self.port} ({e.strerror or e}); "
                "start it with -aethercontrol"
            ) from e
        sock.settimeout(self.timeout)
        self._sock = sock
        file = sock.makefile("rwb")
        self._file = file
        return file

    def close(self) -> None:
        if self._sock is not None:
            try:
                self._sock.close()
            finally:
                self._sock = None
                self._file = None

    def send(self, line: str) -> str:
        """Runs one command and returns its result (the text after `ok`)."""
        line = command_line(line)
        with self._lock:
            for attempt in range(2):
                file = self._file or self._connect()
                try:
                    file.write(line.encode() + b"\n")
                    file.flush()
                    reply = file.readline()
                except TimeoutError as e:
                    # The game still runs the command; a late reply would answer the next one.
                    self.close()
                    raise ControlError(f"timed out waiting for {line!r}") from e
                except OSError:
                    reply = b""
                if reply:
                    break
                # The connection dropped (e.g. the game restarted): retry once on a new one.
                self.close()
                if attempt:
                    raise ControlError(
                        f"the game closed the connection during {line!r}"
                    )
        text = reply.decode().rstrip("\n")
        status, _, result = text.partition(" ")
        if status != "ok":
            raise ControlError(result or text)
        return result

    def ping(self) -> bool:
        try:
            return self.send("ping") == "pong"
        except ControlError:
            return False

    def state(self) -> dict:
        return json.loads(self.send("state"))

    def screenshot(self, path: Path) -> Path:
        path = path.expanduser().resolve()
        self.send(f"screenshot {path}")
        return path


@dataclass
class GameProcess:
    """A client started in the background with the control socket on (for the MCP server)."""

    process: subprocess.Popen
    port: int
    log: Path

    @classmethod
    def start(
        cls,
        port: int = DEFAULT_PORT,
        command: str = "launch",
        options: list[str] | None = None,
        game_args: list[str] | None = None,
        log: Path = LOG,
    ) -> "GameProcess":
        """Runs `aether <command> --control PORT [options] [-- game_args]`, output to log."""
        log.parent.mkdir(parents=True, exist_ok=True)
        cmd = [
            sys.executable,
            "-c",
            "from aether.cli import main; main()",
            command,
            "--control",
            str(port),
            *(options or []),
            *(["--", *game_args] if game_args else []),
        ]
        with log.open("wb") as out:
            process = subprocess.Popen(
                cmd,
                cwd=paths.REPO,
                stdin=subprocess.DEVNULL,
                stdout=out,
                stderr=subprocess.STDOUT,
                # Its own process group, so stopping it also stops the game it started.
                start_new_session=os.name != "nt",
                creationflags=getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0),
            )
        return cls(process, port, log)

    def running(self) -> bool:
        return self.process.poll() is None

    def wait_ready(self, control: Control, timeout: float) -> None:
        """Waits until the game is past its splash screen (building can take minutes)."""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if not self.running():
                raise ControlError(
                    f"the game exited ({self.process.returncode}) before it was ready:\n"
                    + self.log_tail()
                )
            # Answering pings starts during the splash screen; wait for the title menu.
            if control.ping() and not control.state().get("loading"):
                return
            time.sleep(0.5)
        raise ControlError(
            f"the game didn't answer on port {self.port} within {timeout:.0f}s:\n"
            + self.log_tail()
        )

    def stop(self, timeout: float = 10) -> int | None:
        if not self.running():
            return self.process.returncode
        if os.name == "nt":
            self.process.terminate()
        else:
            os.killpg(self.process.pid, signal.SIGTERM)
        try:
            return self.process.wait(timeout)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                self.process.kill()
            else:
                os.killpg(self.process.pid, signal.SIGKILL)
            return self.process.wait(timeout)

    def log_tail(self, lines: int = 30) -> str:
        try:
            text = self.log.read_text(errors="replace")
        except OSError:
            return ""
        return "\n".join(text.splitlines()[-lines:])
