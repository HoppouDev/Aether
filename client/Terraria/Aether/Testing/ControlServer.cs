using Aether.Modding;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.GameInput;

namespace Aether.Testing;

/// <summary>
/// Lets tests and agents drive the game without focusing its window or sending system input. Start
/// the game with <c>-aethercontrol [PORT]</c> (default <see cref="DefaultPort" />): it listens on
/// 127.0.0.1 for one command per line and answers each with one line, <c>ok [RESULT]</c> or
/// <c>error MESSAGE</c>, once the command has finished (a 300 ms drag answers after 300 ms). Commands
/// from all connections run one at a time, in order, on the game's main thread, at the start of each
/// update; their input goes through <see cref="SyntheticInput" />, and the window counts as focused.
/// <c>tools/aether/control.py</c> is the client, and the <c>aether-game</c> MCP server wraps it.
/// <code>
/// mouse move X Y [MS]                  glide there over MS milliseconds (eased; 0 = jump)
/// mouse down|up [left|right|middle]
/// mouse click [BUTTON] [X Y] [FRAMES]  (move there,) press, hold FRAMES updates (default 2), release
/// mouse drag X1 Y1 X2 Y2 [MS] [BUTTON] press at 1, glide to 2 over MS, release
/// mouse wheel NOTCHES                  positive scrolls up
/// key down|up KEY[+KEY...]             XNA Keys names or aliases (ctrl, shift, alt, esc, 0-9, ...)
/// key press KEY[+KEY...] [FRAMES]
/// type TEXT                            text input, as typed (\n is Enter, \t Tab, \\ a backslash)
/// pad connect|disconnect               a synthetic first gamepad (replaces a real one)
/// pad button BUTTON[+BUTTON...] down|up|press [FRAMES]
/// pad stick left|right X Y [MS]        -1..1, Y up; glides over MS
/// pad trigger left|right VALUE [MS]    0..1
/// wait MS | wait frames N
/// screenshot [crop X Y W H] [scale S] PATH
///                                      the next drawn frame as PNG (box-filtered when S < 1);
///                                      answers WxH PATH
/// state                                JSON: screen, menu, player, synthetic input
/// ui layout [interactive]              JSON: visible retained UI and component actions
/// ui act ID ACTION [JSON]              activate, secondary, set_value (0..1) or set_text
/// release                              let go of every button, key, stick and trigger
/// alloc [FRAMES]                       bytes the main thread allocates over FRAMES updates (default 120),
///                                      overall and per retained-mode screen; JSON
/// quit                                 close the game (as its window's close button does)
/// menu MODE                            on the title menu, switch to Main.menuMode MODE
/// profile [FRAMES] [TOP] PATH          with the game started with the allocation profiler (`aether run
///                                      --profile`): record allocation stacks for FRAMES updates
///                                      (default 120) and write the TOP (default 40) sites and methods,
///                                      per frame, to PATH; answers with the report's first line
/// ping
/// </code>
/// </summary>
public static class ControlServer
{
    public const int DefaultPort = 47321;

    private class Command
    {
        public string Line;

        public string Result;

        // Answered already because the game stopped updating; skipped if it ever gets to it.
        public volatile bool Abandoned;

        public readonly ManualResetEvent Done = new ManualResetEvent(initialState: false);
    }

    private class CommandError : Exception
    {
        public CommandError(string message)
            : base(message)
        {
        }
    }

    private static readonly ConcurrentQueue<Command> Pending = new ConcurrentQueue<Command>();

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static bool _started;

    private static Command _current;

    private static IEnumerator<object> _running;

    private static string _screenshotPath;

    private static string _screenshotResult;

    private static Rectangle? _screenshotCrop;

    private static float _screenshotScale;

    private static int _updates;

    private static int _draws;

    // When the main thread last ticked (Clock milliseconds), read by the connection threads.
    private static long _lastTick;

    /// <summary>How long the game can go without an update before waiting commands fail.</summary>
    private const long FrozenMilliseconds = 10000;

    /// <summary>Whether the game was started with <c>-aethercontrol</c>.</summary>
    public static bool IsEnabled => Program.LaunchParameters.ContainsKey("-aethercontrol");

    /// <summary>Starts the server the first time and runs queued commands; called at the start of Main.Update.</summary>
    public static void Tick()
    {
        if (!_started)
        {
            _started = true;
            if (!IsEnabled)
            {
                return;
            }
            Start();
        }
        if (!SyntheticInput.Enabled)
        {
            return;
        }
        _updates++;
        Interlocked.Exchange(ref _lastTick, (long)Clock.Elapsed.TotalMilliseconds);
        // Several commands can finish in one update (e.g. a key down and a mouse move).
        for (int i = 0; i < 64; i++)
        {
            if (_running == null)
            {
                if (!Pending.TryDequeue(out _current))
                {
                    return;
                }
                if (_current.Abandoned)
                {
                    _current = null;
                    continue;
                }
                try
                {
                    _running = Run(_current.Line).GetEnumerator();
                }
                catch (CommandError error)
                {
                    Finish("error " + error.Message);
                    continue;
                }
            }
            bool more;
            try
            {
                more = _running.MoveNext();
            }
            catch (CommandError error)
            {
                Finish("error " + error.Message);
                continue;
            }
            catch (Exception exception)
            {
                Finish("error " + exception.GetType().Name + ": " + exception.Message.Replace('\n', ' '));
                continue;
            }
            if (more)
            {
                return;
            }
            Finish("ok" + (_current.Result == null ? "" : " " + _current.Result));
        }
    }

    private static void Finish(string response)
    {
        _current.Result = response;
        _current.Done.Set();
        _current = null;
        _running = null;
    }

    private static void Start()
    {
        int port = DefaultPort;
        string value = Program.LaunchParameters["-aethercontrol"];
        if (value != "" && !int.TryParse(value, out port))
        {
            AetherLog.For("Aether.Control").Warning("-aethercontrol: {Value} isn't a port; using {Port}.", value, DefaultPort);
            port = DefaultPort;
        }
        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            AetherLog.For("Aether.Control").Warning("-aethercontrol: can't listen on 127.0.0.1:{Port}: {Reason}", port, exception.Message);
            return;
        }
        SyntheticInput.Enabled = true;
        SyntheticInput.MousePosition = new Vector2(Main.screenWidth / 2, Main.screenHeight / 2);
        Main.OnPostDraw += AfterDraw;
        Thread thread = new Thread(() => Accept(listener));
        thread.IsBackground = true;
        thread.Name = "Aether control";
        thread.Start();
        AetherLog.For("Aether.Control").Information("Listening on 127.0.0.1:{Port}", port);
    }

    private static void Accept(TcpListener listener)
    {
        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            Thread thread = new Thread(() => Serve(client));
            thread.IsBackground = true;
            thread.Name = "Aether control client";
            thread.Start();
        }
    }

    private static void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                StreamReader reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.NewLine = "\n";
                writer.AutoFlush = true;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.TrimEnd('\r');
                    if (line.Trim() == "")
                    {
                        continue;
                    }
                    Command command = new Command
                    {
                        Line = line
                    };
                    Pending.Enqueue(command);
                    while (!command.Done.WaitOne(1000))
                    {
                        if ((long)Clock.Elapsed.TotalMilliseconds - Interlocked.Read(ref _lastTick) > FrozenMilliseconds)
                        {
                            command.Abandoned = true;
                            break;
                        }
                    }
                    writer.WriteLine(command.Abandoned && !command.Done.WaitOne(0) ? "error the game stopped updating (frozen, or showing an error dialog)" : command.Result);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static IEnumerable<object> Run(string line)
    {
        string[] words = line.Trim().Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = words[0].ToLowerInvariant();
        string sub = words.Length > 1 ? words[1].ToLowerInvariant() : "";
        switch (verb)
        {
        case "ping":
            return Result("pong");
        case "state":
            return Result(State());
        case "ui":
            return Ui(line);
        case "alloc":
            return Alloc((int)Optional(words, 1, 120f));
        case "profile":
            return Profile(words, line);
        case "menu":
            if (!Main.gameMenu)
            {
                throw new CommandError("not on the title menu");
            }
            Main.menuMode = Int(words, 1, "menuMode");
            return Frames(2);
        case "quit":
            Main.instance.Exit();
            return Frames(0);
        case "release":
            SyntheticInput.ReleaseAll();
            return Frames(1);
        case "wait":
            if (sub == "frames")
            {
                return Frames(Int(words, 2, "frames"));
            }
            return Wait(Number(words, 1, "milliseconds"));
        case "screenshot":
            return Screenshot(words, line);
        case "type":
            return Type(line.TrimStart().Substring(4).TrimStart(' '));
        case "mouse":
            return Mouse(sub, words);
        case "key":
            return Key(sub, words);
        case "pad":
            return Pad(sub, words);
        default:
            throw new CommandError("unknown command " + verb);
        }
    }

    private static IEnumerable<object> Mouse(string sub, string[] words)
    {
        switch (sub)
        {
        case "move":
            if (Point(words, 2) == SyntheticInput.MousePosition)
            {
                return Jump(SyntheticInput.MousePosition);
            }
            return Glide(() => SyntheticInput.MousePosition, v => SyntheticInput.MousePosition = v, Point(words, 2), Optional(words, 4, 0f));
        case "down":
        case "up":
            SetMouseButton(MouseButton(words, 2), sub == "down");
            return Frames(1);
        case "click":
            return Click(words);
        case "drag":
            return Drag(Point(words, 2), Point(words, 4), Optional(words, 6, 300f), MouseButton(words, 7));
        case "wheel":
            SyntheticInput.ScrollWheel += Int(words, 2, "notches") * 120;
            return Frames(1);
        default:
            throw new CommandError("mouse move|down|up|click|drag|wheel");
        }
    }

    private static IEnumerable<object> Click(string[] words)
    {
        // mouse click [BUTTON] [X Y] [FRAMES]: the button is the only word that isn't a number.
        int index = 2;
        string button = "left";
        if (words.Length > index && !IsNumber(words[index]))
        {
            button = words[index++];
        }
        Vector2? at = null;
        if (words.Length > index + 1)
        {
            at = Point(words, index);
            index += 2;
        }
        int frames = (int)Optional(words, index, 2f);
        MouseButton(new string[1] { button }, 0);
        if (at.HasValue)
        {
            foreach (object step in Jump(at.Value))
            {
                yield return step;
            }
        }
        SetMouseButton(button, down: true);
        for (int i = 0; i < Math.Max(1, frames); i++)
        {
            yield return null;
        }
        SetMouseButton(button, down: false);
        yield return null;
    }

    private static IEnumerable<object> Drag(Vector2 from, Vector2 to, float milliseconds, string button)
    {
        foreach (object step in Jump(from))
        {
            yield return step;
        }
        SetMouseButton(button, down: true);
        yield return null;
        yield return null;
        foreach (object step in Glide(() => SyntheticInput.MousePosition, v => SyntheticInput.MousePosition = v, to, milliseconds))
        {
            yield return step;
        }
        yield return null;
        SetMouseButton(button, down: false);
        yield return null;
    }

    /// <summary>
    /// Puts the cursor at <c>position</c> by way of a pixel beside it, so the game always sees it
    /// move: it only takes the mouse's position (and leaves gamepad mode) when the mouse moves.
    /// </summary>
    private static IEnumerable<object> Jump(Vector2 position)
    {
        SyntheticInput.MousePosition = position + new Vector2(position.X >= 1f ? -1f : 1f, 0f);
        yield return null;
        SyntheticInput.MousePosition = position;
        yield return null;
    }

    private static IEnumerable<object> Key(string sub, string[] words)
    {
        if (words.Length < 3)
        {
            throw new CommandError("key down|up|press KEY[+KEY...] [FRAMES]");
        }
        List<Keys> keys = new List<Keys>();
        foreach (string name in words[2].Split('+'))
        {
            keys.Add(ParseKey(name));
        }
        switch (sub)
        {
        case "down":
            SyntheticInput.KeysDown.UnionWith(keys);
            return Frames(1);
        case "up":
            SyntheticInput.KeysDown.ExceptWith(keys);
            return Frames(1);
        case "press":
            return KeyPress(keys, (int)Optional(words, 3, 2f));
        default:
            throw new CommandError("key down|up|press");
        }
    }

    private static IEnumerable<object> KeyPress(List<Keys> keys, int frames)
    {
        // Modifiers first, so e.g. ctrl+shift+l reads as a chord.
        SyntheticInput.KeysDown.UnionWith(keys);
        for (int i = 0; i < Math.Max(1, frames); i++)
        {
            yield return null;
        }
        SyntheticInput.KeysDown.ExceptWith(keys);
        yield return null;
    }

    private static IEnumerable<object> Type(string text)
    {
        StringBuilder unescaped = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char next = text[++i];
                unescaped.Append(next switch
                {
                    'n' => '\r',
                    't' => '\t',
                    'e' => '\u001b',
                    _ => next
                });
            }
            else
            {
                unescaped.Append(c);
            }
        }
        // The game reads the typed characters once per frame, from a 100-character buffer.
        foreach (char c in unescaped.ToString())
        {
            if (Main.keyCount >= Main.keyInt.Length)
            {
                yield return null;
            }
            SyntheticInput.TypeCharacter(c);
        }
        yield return null;
        yield return null;
    }

    private static IEnumerable<object> Pad(string sub, string[] words)
    {
        switch (sub)
        {
        case "connect":
        case "disconnect":
            SyntheticInput.GamePadConnected = sub == "connect";
            if (sub == "disconnect")
            {
                SyntheticInput.GamePadButtons = (Buttons)0;
                SyntheticInput.LeftStick = (SyntheticInput.RightStick = Vector2.Zero);
                SyntheticInput.LeftTrigger = (SyntheticInput.RightTrigger = 0f);
            }
            return Frames(1);
        case "button":
            return PadButton(words);
        case "stick":
        {
            bool left = Side(words);
            Vector2 target = Point(words, 3);
            if (target.Length() > 1f)
            {
                target.Normalize();
            }
            if (left)
            {
                return Glide(() => SyntheticInput.LeftStick, v => SyntheticInput.LeftStick = v, target, Optional(words, 5, 0f));
            }
            return Glide(() => SyntheticInput.RightStick, v => SyntheticInput.RightStick = v, target, Optional(words, 5, 0f));
        }
        case "trigger":
        {
            bool left = Side(words);
            float value = MathHelper.Clamp(Number(words, 3, "value"), 0f, 1f);
            if (left)
            {
                return Glide(() => new Vector2(SyntheticInput.LeftTrigger, 0f), v => SyntheticInput.LeftTrigger = v.X, new Vector2(value, 0f), Optional(words, 4, 0f));
            }
            return Glide(() => new Vector2(SyntheticInput.RightTrigger, 0f), v => SyntheticInput.RightTrigger = v.X, new Vector2(value, 0f), Optional(words, 4, 0f));
        }
        default:
            throw new CommandError("pad connect|disconnect|button|stick|trigger");
        }
    }

    private static IEnumerable<object> PadButton(string[] words)
    {
        if (words.Length < 4)
        {
            throw new CommandError("pad button BUTTON[+BUTTON...] down|up|press [FRAMES]");
        }
        if (!SyntheticInput.GamePadConnected)
        {
            throw new CommandError("no synthetic gamepad (pad connect)");
        }
        Buttons buttons = (Buttons)0;
        foreach (string name in words[2].Split('+'))
        {
            buttons |= ParseButton(name);
        }
        string action = words[3].ToLowerInvariant();
        switch (action)
        {
        case "down":
            SyntheticInput.GamePadButtons |= buttons;
            return Frames(1);
        case "up":
            SyntheticInput.GamePadButtons &= ~buttons;
            return Frames(1);
        case "press":
            return PadPress(buttons, (int)Optional(words, 4, 2f));
        default:
            throw new CommandError("pad button BUTTON down|up|press");
        }
    }

    private static IEnumerable<object> PadPress(Buttons buttons, int frames)
    {
        SyntheticInput.GamePadButtons |= buttons;
        for (int i = 0; i < Math.Max(1, frames); i++)
        {
            yield return null;
        }
        SyntheticInput.GamePadButtons &= ~buttons;
        yield return null;
    }

    private static bool Side(string[] words)
    {
        string side = words.Length > 2 ? words[2].ToLowerInvariant() : "";
        if (side != "left" && side != "right")
        {
            throw new CommandError("pad " + words[1] + " left|right ...");
        }
        if (!SyntheticInput.GamePadConnected)
        {
            throw new CommandError("no synthetic gamepad (pad connect)");
        }
        return side == "left";
    }

    /// <summary>Moves a value to <c>target</c> over <c>milliseconds</c> (smoothstep easing), a step per update.</summary>
    private static IEnumerable<object> Glide(Func<Vector2> get, Action<Vector2> set, Vector2 target, float milliseconds)
    {
        Vector2 start = get();
        double began = Clock.Elapsed.TotalMilliseconds;
        while (milliseconds > 0f)
        {
            float t = (float)((Clock.Elapsed.TotalMilliseconds - began) / milliseconds);
            if (t >= 1f)
            {
                break;
            }
            set(Vector2.Lerp(start, target, t * t * (3f - 2f * t)));
            yield return null;
        }
        set(target);
        yield return null;
    }

    private static IEnumerable<object> Alloc(int frames)
    {
        if (!AllocationProfiler.Available)
        {
            throw new CommandError("this runtime can't count allocations (no GC.GetAllocatedBytesForCurrentThread)");
        }
        frames = Math.Max(1, frames);
        AllocationProfiler.Reset();
        AllocationProfiler.Active = true;
        int collections = GC.CollectionCount(0);
        int drawn = _draws;
        long start = AllocationProfiler.Now();
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
        long total = AllocationProfiler.Now() - start;
        AllocationProfiler.Active = false;
        drawn = Math.Max(1, _draws - drawn);
        List<KeyValuePair<object, long>> screens = new List<KeyValuePair<object, long>>(AllocationProfiler.ByScreen);
        screens.Sort((a, b) => b.Value.CompareTo(a.Value));
        Dictionary<string, long> perScreen = new Dictionary<string, long>();
        long screensTotal = 0;
        foreach (KeyValuePair<object, long> screen in screens)
        {
            // Screens by type; sections a screen measures itself (AllocationProfiler.Add with a string) by name.
            string name = screen.Key as string ?? screen.Key.GetType().Name;
            perScreen.TryGetValue(name, out var bytes);
            perScreen[name] = bytes + screen.Value / drawn;
            screensTotal += screen.Value;
        }
        _current.Result = JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["updates"] = frames,
            ["draws"] = drawn,
            ["bytesPerUpdate"] = total / frames,
            ["screenBytesPerDraw"] = screensTotal / drawn,
            ["gen0Collections"] = GC.CollectionCount(0) - collections,
            ["screensPerDraw"] = perScreen
        });
    }

    private static IEnumerable<object> Profile(string[] words, string line)
    {
        if (!NativeProfiler.Available)
        {
            throw new CommandError("the allocation profiler isn't loaded; start the game with --profile");
        }
        // profile [FRAMES] [TOP] PATH: the numbers first, the rest of the line is the path.
        int index = 1;
        int frames = 120;
        int top = 40;
        if (words.Length > index + 1 && IsNumber(words[index]))
        {
            frames = Math.Max(1, Int(words, index++, "frames"));
            if (words.Length > index + 1 && IsNumber(words[index]))
            {
                top = Math.Max(1, Int(words, index++, "top"));
            }
        }
        if (words.Length <= index)
        {
            throw new CommandError("profile [FRAMES] [TOP] PATH");
        }
        string rest = line.Trim().Substring(words[0].Length).TrimStart();
        for (int i = 1; i < index; i++)
        {
            rest = rest.Substring(words[i].Length).TrimStart();
        }
        string path = Path.GetFullPath(rest);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        NativeProfiler.Start();
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
        NativeProfiler.Stop();
        if (!NativeProfiler.Report(path, top, frames))
        {
            throw new CommandError("can't write " + path);
        }
        using (StreamReader reader = new StreamReader(path))
        {
            _current.Result = reader.ReadLine() + " -> " + path;
        }
    }

    private static IEnumerable<object> Wait(float milliseconds)
    {
        double until = Clock.Elapsed.TotalMilliseconds + milliseconds;
        while (Clock.Elapsed.TotalMilliseconds < until)
        {
            yield return null;
        }
    }

    private static IEnumerable<object> Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerable<object> Result(string result)
    {
        _current.Result = result;
        yield break;
    }

    private static IEnumerable<object> Ui(string line)
    {
        string[] parts = line.Trim().Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        bool layout = parts.Length >= 2 && parts[1].Equals("layout", StringComparison.OrdinalIgnoreCase);
        bool interactiveOnly = parts.Length == 3 && parts[2].Equals("interactive", StringComparison.OrdinalIgnoreCase);
        UiAutomation.Actions action = UiAutomation.Actions.None;
        JToken value = null;
        if (layout)
        {
            if (parts.Length != 2 && !interactiveOnly)
            {
                throw new CommandError("ui layout [interactive]");
            }
        }
        else
        {
            if (parts.Length < 4 || !parts[1].Equals("act", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandError("ui layout [interactive] | ui act ID ACTION [JSON]");
            }
            action = parts[3] switch
            {
                "activate" => UiAutomation.Actions.Activate,
                "secondary" => UiAutomation.Actions.Secondary,
                "set_value" => UiAutomation.Actions.SetValue,
                "set_text" => UiAutomation.Actions.SetText,
                _ => throw new CommandError("unknown UI action; use activate, secondary, set_value or set_text")
            };
            if (action is UiAutomation.Actions.SetValue or UiAutomation.Actions.SetText)
            {
                if (parts.Length != 5)
                {
                    throw new CommandError("this UI action requires a JSON value");
                }
                try
                {
                    value = JsonConvert.DeserializeObject<JToken>(parts[4], new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                }
                catch (JsonException)
                {
                    throw new CommandError("invalid JSON value for UI action");
                }
                if (value == null)
                {
                    throw new CommandError("invalid JSON value for UI action");
                }
                if (action == UiAutomation.Actions.SetValue &&
                    (value.Type is not (JTokenType.Float or JTokenType.Integer) ||
                     !(value.Value<double>() is >= 0.0 and <= 1.0)))
                {
                    throw new CommandError("set_value requires a number from 0 to 1");
                }
                if (action == UiAutomation.Actions.SetText && value.Type != JTokenType.String)
                {
                    throw new CommandError("set_text requires a JSON string");
                }
            }
            else if (parts.Length != 4)
            {
                throw new CommandError("this UI action does not take a value");
            }
        }

        UiAutomation.Frame frame = UiAutomation.Capture(layout ? null : parts[2]);
        if (frame == null)
        {
            throw new CommandError("stale UI component; request the current layout");
        }
        double deadline = Clock.Elapsed.TotalMilliseconds + 2000;
        try
        {
            while (!frame.Finished)
            {
                if (Clock.Elapsed.TotalMilliseconds >= deadline)
                {
                    throw new CommandError("no UI frame was drawn");
                }
                yield return null;
            }
            if (_current.Abandoned)
            {
                yield break;
            }
            if (frame.Error != null)
            {
                throw new CommandError("could not read the UI: " + frame.Error.Message.Replace('\n', ' '));
            }
            if (layout)
            {
                _current.Result = UiAutomation.Publish(frame, interactiveOnly);
            }
            else if (!UiAutomation.TryAct(frame, parts[2], action, value, out string error))
            {
                throw new CommandError(error);
            }
        }
        finally
        {
            UiAutomation.Cancel(frame);
        }
    }

    private static IEnumerable<object> Screenshot(string[] words, string line)
    {
        // Options first; the rest of the line is the path (which may contain spaces).
        int index = 1;
        Rectangle? crop = null;
        float scale = 1f;
        while (words.Length > index + 1)
        {
            string option = words[index].ToLowerInvariant();
            if (option == "crop")
            {
                crop = new Rectangle(Int(words, index + 1, "x"), Int(words, index + 2, "y"), Int(words, index + 3, "width"), Int(words, index + 4, "height"));
                index += 5;
            }
            else if (option == "scale")
            {
                scale = MathHelper.Clamp(Number(words, index + 1, "scale"), 0.05f, 1f);
                index += 2;
            }
            else
            {
                break;
            }
        }
        if (words.Length <= index)
        {
            throw new CommandError("screenshot [crop X Y W H] [scale S] PATH");
        }
        string rest = line.Trim().Substring(words[0].Length).TrimStart();
        for (int i = 1; i < index; i++)
        {
            rest = rest.Substring(words[i].Length).TrimStart();
        }
        _screenshotCrop = crop;
        _screenshotScale = scale;
        _screenshotResult = null;
        _screenshotPath = Path.GetFullPath(rest);
        while (_screenshotPath != null)
        {
            yield return null;
        }
        if (_screenshotResult.StartsWith("error "))
        {
            throw new CommandError(_screenshotResult.Substring(6));
        }
        _current.Result = _screenshotResult;
    }

    private static void AfterDraw(GameTime gameTime)
    {
        _draws++;
        UiAutomation.FinishFrame(Main.instance.GraphicsDevice);
        if (_screenshotPath == null)
        {
            return;
        }
        try
        {
            GraphicsDevice device = Main.instance.GraphicsDevice;
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            Color[] frame = new Color[width * height];
            device.GetBackBufferData(frame);
            Rectangle area = Rectangle.Intersect(_screenshotCrop ?? new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height));
            if (area.Width <= 0 || area.Height <= 0)
            {
                throw new InvalidOperationException("the crop is outside the " + width + "x" + height + " frame");
            }
            Color[] pixels = Shrink(frame, width, area, _screenshotScale, out width, out height);
            Directory.CreateDirectory(Path.GetDirectoryName(_screenshotPath));
            using (Texture2D texture = new Texture2D(device, width, height))
            {
                texture.SetData(pixels);
                using FileStream stream = File.Create(_screenshotPath);
                texture.SaveAsPng(stream, width, height);
            }
            _screenshotResult = width + "x" + height + " " + _screenshotPath;
        }
        catch (Exception exception)
        {
            _screenshotResult = "error " + exception.GetType().Name + ": " + exception.Message.Replace('\n', ' ');
        }
        _screenshotPath = null;
    }

    /// <summary>Crops the frame to <c>area</c> and box-filters it down by <c>scale</c>, with opaque alpha.</summary>
    private static Color[] Shrink(Color[] frame, int stride, Rectangle area, float scale, out int width, out int height)
    {
        width = Math.Max(1, (int)Math.Round(area.Width * scale));
        height = Math.Max(1, (int)Math.Round(area.Height * scale));
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            int top = area.Y + y * area.Height / height;
            int bottom = Math.Max(top + 1, area.Y + (y + 1) * area.Height / height);
            for (int x = 0; x < width; x++)
            {
                int left = area.X + x * area.Width / width;
                int right = Math.Max(left + 1, area.X + (x + 1) * area.Width / width);
                int r = 0;
                int g = 0;
                int b = 0;
                for (int sy = top; sy < bottom; sy++)
                {
                    for (int sx = left; sx < right; sx++)
                    {
                        Color c = frame[sy * stride + sx];
                        r += c.R;
                        g += c.G;
                        b += c.B;
                    }
                }
                int count = (bottom - top) * (right - left);
                pixels[y * width + x] = new Color(r / count, g / count, b / count, 255);
            }
        }
        return pixels;
    }

    private static string State()
    {
        Dictionary<string, object> state = new Dictionary<string, object>
        {
            ["screen"] = new int[2] { Main.screenWidth, Main.screenHeight },
            ["uiScale"] = Main.UIScale,
            ["loading"] = Main.showSplash,
            ["gameMenu"] = Main.gameMenu,
            ["menuMode"] = Main.menuMode,
            ["updates"] = _updates,
            ["fps"] = Main.frameRate,
            ["ups"] = Main.updateRate,
            ["presentInterval"] = Main.instance.GraphicsDevice.PresentationParameters.PresentationInterval.ToString(),
            ["mouse"] = new float[2] { SyntheticInput.MousePosition.X, SyntheticInput.MousePosition.Y },
            ["mouseButtons"] = new bool[3] { SyntheticInput.LeftButton, SyntheticInput.RightButton, SyntheticInput.MiddleButton },
            ["mouseUI"] = new float[2] { Main.mouseX, Main.mouseY },
            ["keysDown"] = SyntheticKeyNames(),
            ["usingGamepad"] = PlayerInput.UsingGamepad,
            ["inputMode"] = PlayerInput.CurrentInputMode.ToString(),
            ["pad"] = SyntheticInput.GamePadConnected ? new Dictionary<string, object>
            {
                ["buttons"] = SyntheticInput.GamePadButtons.ToString(),
                ["leftStick"] = new float[2] { SyntheticInput.LeftStick.X, SyntheticInput.LeftStick.Y },
                ["rightStick"] = new float[2] { SyntheticInput.RightStick.X, SyntheticInput.RightStick.Y },
                ["triggers"] = new float[2] { SyntheticInput.LeftTrigger, SyntheticInput.RightTrigger }
            } : null
        };
        if (!Main.gameMenu && Main.myPlayer >= 0)
        {
            Player player = Main.LocalPlayer;
            state["player"] = new Dictionary<string, object>
            {
                ["name"] = player.name,
                ["tile"] = new int[2] { (int)(player.Center.X / 16f), (int)(player.Center.Y / 16f) },
                ["life"] = new int[2] { player.statLife, player.statLifeMax2 },
                ["mana"] = new int[2] { player.statMana, player.statManaMax2 },
                ["selectedItem"] = player.selectedItem,
                ["heldItem"] = player.HeldItem?.Name,
                ["inventoryOpen"] = Main.playerInventory,
                ["chest"] = player.chest,
                ["talkNPC"] = player.talkNPC,
                ["mouseItem"] = Main.mouseItem?.IsAir == false ? Main.mouseItem.Name : null
            };
            state["ingameOptions"] = Main.ingameOptionsWindow;
            state["mapFullscreen"] = Main.mapFullscreen;
        }
        return JsonConvert.SerializeObject(state);
    }

    private static List<string> SyntheticKeyNames()
    {
        List<string> names = new List<string>();
        foreach (Keys key in SyntheticInput.KeysDown)
        {
            names.Add(key.ToString());
        }
        return names;
    }

    private static readonly Dictionary<string, Keys> KeyAliases = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Keys.LeftControl,
        ["control"] = Keys.LeftControl,
        ["shift"] = Keys.LeftShift,
        ["alt"] = Keys.LeftAlt,
        ["esc"] = Keys.Escape,
        ["return"] = Keys.Enter,
        ["backspace"] = Keys.Back,
        ["del"] = Keys.Delete,
        ["pgup"] = Keys.PageUp,
        ["pgdn"] = Keys.PageDown,
        ["-"] = Keys.OemMinus,
        ["="] = Keys.OemPlus,
        ["["] = Keys.OemOpenBrackets,
        ["]"] = Keys.OemCloseBrackets,
        [";"] = Keys.OemSemicolon,
        ["'"] = Keys.OemQuotes,
        [","] = Keys.OemComma,
        ["."] = Keys.OemPeriod,
        ["/"] = Keys.OemQuestion,
        ["`"] = Keys.OemTilde,
        ["\\"] = Keys.OemPipe
    };

    private static Keys ParseKey(string name)
    {
        if (KeyAliases.TryGetValue(name, out var key))
        {
            return key;
        }
        if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
        {
            return Keys.D0 + (name[0] - '0');
        }
        if (!IsNumber(name) && Enum.TryParse<Keys>(name, ignoreCase: true, out key))
        {
            return key;
        }
        throw new CommandError("unknown key " + name);
    }

    private static readonly Dictionary<string, Buttons> ButtonAliases = new Dictionary<string, Buttons>(StringComparer.OrdinalIgnoreCase)
    {
        ["lb"] = Buttons.LeftShoulder,
        ["rb"] = Buttons.RightShoulder,
        ["lt"] = Buttons.LeftTrigger,
        ["rt"] = Buttons.RightTrigger,
        ["ls"] = Buttons.LeftStick,
        ["rs"] = Buttons.RightStick,
        ["select"] = Buttons.Back,
        ["up"] = Buttons.DPadUp,
        ["down"] = Buttons.DPadDown,
        ["left"] = Buttons.DPadLeft,
        ["right"] = Buttons.DPadRight
    };

    private static Buttons ParseButton(string name)
    {
        if (ButtonAliases.TryGetValue(name, out var button))
        {
            return button;
        }
        if (!IsNumber(name) && Enum.TryParse<Buttons>(name, ignoreCase: true, out button))
        {
            return button;
        }
        throw new CommandError("unknown button " + name);
    }

    private static string MouseButton(string[] words, int index)
    {
        string button = words.Length > index ? words[index].ToLowerInvariant() : "left";
        if (button != "left" && button != "right" && button != "middle")
        {
            throw new CommandError("unknown mouse button " + button);
        }
        return button;
    }

    private static void SetMouseButton(string button, bool down)
    {
        switch (button.ToLowerInvariant())
        {
        case "right":
            SyntheticInput.RightButton = down;
            break;
        case "middle":
            SyntheticInput.MiddleButton = down;
            break;
        default:
            SyntheticInput.LeftButton = down;
            break;
        }
    }

    private static bool IsNumber(string word)
    {
        return float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static float Number(string[] words, int index, string what)
    {
        if (words.Length <= index || !float.TryParse(words[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new CommandError("expected " + what + " at word " + (index + 1));
        }
        return value;
    }

    private static int Int(string[] words, int index, string what)
    {
        return (int)Math.Round(Number(words, index, what));
    }

    private static float Optional(string[] words, int index, float fallback)
    {
        if (words.Length <= index || !IsNumber(words[index]))
        {
            return fallback;
        }
        return Number(words, index, "number");
    }

    private static Vector2 Point(string[] words, int index)
    {
        return new Vector2(Number(words, index, "x"), Number(words, index + 1, "y"));
    }
}
