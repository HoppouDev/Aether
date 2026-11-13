using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Aether.Testing;

/// <summary>
/// The <c>aether-game</c> MCP server's tools (<see cref="ControlServer" /> serves them with
/// <c>-aethermcp</c>). Each runs control commands with <see cref="ControlServer.Execute" /> and returns
/// once their input has finished. Coordinates are window pixels, as a full-size screenshot's.
/// </summary>
// Parameters are snake_case: they're the tools' argument names.
[McpServerToolType]
public static class GameTools
{
    public const string Instructions = """
        Drives the running Aether (Terraria) client with synthetic mouse, keyboard and gamepad input
        that bypasses the window system: the window needn't have focus and the real cursor never moves.
        The server is the client itself (started with ./aether run --mcp, or launch --mcp).
        Use game_ui_layout and game_ui_action for direct interaction with retained controls.
        Use game_screenshot and the mouse_/key_/type_/gamepad_ tools for other areas.
        Coordinates are window pixels, the same as a full-size screenshot's (game_state gives the
        screen size; scale a scaled screenshot's coordinates back up). Every tool returns once its
        input has finished (a 400 ms drag returns after 400 ms). game_send runs raw control commands
        (see client/Terraria/Aether/Testing/ControlServer.cs) in one call, for sequences.
        For performance, frame_stats measures frame, CPU and GPU times and the .NET runtime per frame
        (MangoHud-style, optionally as CSV), overlay shows the profiler overlay, and game_profile (with
        the client started with --profile) finds the stacks that allocate.
        """;

    // Screenshots and reports go to files the game writes and the tools read back.
    private static readonly string Scratch = Directory.CreateTempSubdirectory("aether-mcp-").FullName;

    private static int _files;

    [McpServerTool(Name = "game_quit", Destructive = true)]
    [Description("Close the client (unsaved progress is lost).")]
    public static async Task<string> Quit()
    {
        await Run("quit");
        return "closing";
    }

    [McpServerTool(Name = "game_state", ReadOnly = true)]
    [Description("Screen size, title-menu mode, the frame and update rates, synthetic input, and in game the player's state, as JSON.")]
    public static Task<string> State() => Run("state");

    [McpServerTool(Name = "game_ui_layout", ReadOnly = true)]
    [Description("Inspect the next rendered UI frame, as JSON. Each component tree is listed separately under trees, with its id and owner: Terraria's retained UI (terraria) and mods' own (e.g. gum, FreshUI's Gum screens; their component IDs start with the tree's id, like gum:12). Components include IDs, parents, labels, bounds, values and available actions. Set interactive_only to return only actionable controls. IDs belong to the latest layout.")]
    public static Task<string> UiLayout(bool interactive_only = false) => Run("ui layout", interactive_only ? "interactive" : null);

    [McpServerTool(Name = "game_ui_action")]
    [Description("Act on a component from game_ui_layout without moving the cursor. Use a number from 0 to 1 for set_value and a string for set_text. Refresh after UI changes.")]
    public static async Task<string> UiAction(
        [Description("A component ID from game_ui_layout, like 0a1b2c3d-12.")] string component_id,
        [Description("activate, secondary, set_value or set_text.")] string action = "activate",
        [Description("For set_value a number, for set_text a string.")] JsonElement? value = null)
    {
        await Run("ui act", component_id, action, value?.GetRawText());
        return "ok";
    }

    [McpServerTool(Name = "game_screenshot", ReadOnly = true)]
    [Description("Capture the next drawn frame (the game's own back buffer, not the screen).")]
    public static async Task<CallToolResult> Screenshot(
        [Description("Downscale factor (0 to 1); multiply coordinates read off the image by 1/scale (relative to the crop's origin) before clicking.")] float scale = 0.5f,
        [Description("[x, y, width, height] in window pixels, to look closely.")] int[] crop = null,
        [Description("A component ID from game_ui_layout (any tree), to capture just it.")] string component_id = null,
        [Description("Window pixels to include around the component.")] int padding = 0)
    {
        if (scale is <= 0f or > 1f)
        {
            throw new McpException("scale is above 0 and at most 1");
        }
        if (crop != null && crop.Length != 4)
        {
            throw new McpException("crop is [x, y, width, height]");
        }
        (string path, string size) = await Shot(scale, crop, component_id, padding);
        return new CallToolResult
        {
            Content = [ImageContentBlock.FromBytes(File.ReadAllBytes(path), "image/png"), new TextContentBlock { Text = size }]
        };
    }

    [McpServerTool(Name = "mouse_move")]
    [Description("Move the synthetic cursor to (x, y), gliding (eased) over duration_ms.")]
    public static Task<string> MouseMove(float x, float y, float duration_ms = 0) => Ok("mouse move", x, y, NonNegative(duration_ms));

    [McpServerTool(Name = "mouse_click")]
    [Description("Click (at (x, y), else where the cursor is), holding the button hold_frames updates.")]
    public static async Task<string> MouseClick(
        float? x = null,
        float? y = null,
        [Description("left, right or middle.")] string button = "left",
        int hold_frames = 2,
        [Description("Clicks in a row (1 to 10).")] int count = 1)
    {
        if (count is < 1 or > 10)
        {
            throw new McpException("count is 1 to 10");
        }
        for (int i = 0; i < count; i++)
        {
            bool at = x.HasValue && y.HasValue && i == 0;
            await Run("mouse click", button, at ? x : null, at ? y : null, AtLeastOne(hold_frames));
        }
        return "ok";
    }

    [McpServerTool(Name = "mouse_drag")]
    [Description("Press at (x1, y1), glide to (x2, y2) over duration_ms, release.")]
    public static Task<string> MouseDrag(float x1, float y1, float x2, float y2, float duration_ms = 400, string button = "left") =>
        Ok("mouse drag", x1, y1, x2, y2, NonNegative(duration_ms), button);

    [McpServerTool(Name = "mouse_button")]
    [Description("Press (down=true) or release a mouse button and leave it there (for long holds).")]
    public static Task<string> MouseButton(string button, bool down) => Ok("mouse", down ? "down" : "up", button);

    [McpServerTool(Name = "mouse_wheel")]
    [Description("Scroll the wheel; positive is up (away from you).")]
    public static Task<string> MouseWheel(int notches) => Ok("mouse wheel", notches);

    [McpServerTool(Name = "key_press")]
    [Description("Press a key or chord and release it.")]
    public static async Task<string> KeyPress(
        [Description("XNA key names joined by '+', e.g. 'Escape', 'ctrl+shift+l', 'E', 'D1', 'Space'. Aliases: ctrl, shift, alt, esc, enter, backspace, 0-9.")] string keys,
        int hold_frames = 2,
        [Description("Hold for this long instead (e.g. to walk).")] float? hold_ms = null)
    {
        if (hold_ms == null)
        {
            return await Ok("key press", keys, AtLeastOne(hold_frames));
        }
        await Run("key down", keys);
        try
        {
            await Run("wait", NonNegative(hold_ms.Value));
        }
        finally
        {
            await Run("key up", keys);
        }
        return "ok";
    }

    [McpServerTool(Name = "key_hold")]
    [Description("Hold keys down (down=true) or release them; they stay until released.")]
    public static Task<string> KeyHold(string keys, bool down) => Ok("key", down ? "down" : "up", keys);

    [McpServerTool(Name = "type_text")]
    [Description("Type text into the focused text field (a newline is Enter).")]
    public static Task<string> TypeText(string text) => Ok("type", EscapeText(text));

    [McpServerTool(Name = "gamepad_connect")]
    [Description("Plug in (or unplug) the synthetic gamepad; it replaces a real first gamepad.")]
    public static Task<string> GamepadConnect(bool connected = true) => Ok("pad", connected ? "connect" : "disconnect");

    [McpServerTool(Name = "gamepad_button")]
    [Description("Press, hold down or release gamepad buttons (connects the pad if needed).")]
    public static async Task<string> GamepadButton(
        [Description("XNA Buttons joined by '+': A, B, X, Y, Start, Back, LeftShoulder/lb, RightShoulder/rb, LeftStick/ls, RightStick/rs, DPadUp/up, DPadDown/down, DPadLeft/left, DPadRight/right, lt, rt.")] string buttons,
        [Description("press, down or up.")] string action = "press",
        int hold_frames = 2)
    {
        await Connect();
        return await Ok("pad button", buttons, action, AtLeastOne(hold_frames));
    }

    [McpServerTool(Name = "gamepad_stick")]
    [Description("Move a thumbstick smoothly to (x, y), each from -1 to 1 with up positive (connects the pad if needed).")]
    public static async Task<string> GamepadStick(
        [Description("left or right.")] string stick,
        float x,
        float y,
        float duration_ms = 150,
        [Description("Hold there this long, then glide back to centre over duration_ms; omit to leave the stick there.")] float? hold_ms = null)
    {
        await Connect();
        await Run("pad stick", stick, Math.Clamp(x, -1f, 1f), Math.Clamp(y, -1f, 1f), NonNegative(duration_ms));
        if (hold_ms != null)
        {
            await Run("wait", NonNegative(hold_ms.Value));
            await Run("pad stick", stick, 0, 0, NonNegative(duration_ms));
        }
        return "ok";
    }

    [McpServerTool(Name = "gamepad_trigger")]
    [Description("Pull a trigger smoothly to value, from 0 to 1 (connects the pad if needed).")]
    public static async Task<string> GamepadTrigger(
        [Description("left or right.")] string trigger,
        float value,
        float duration_ms = 100,
        [Description("Hold this long, then release; omit to leave it.")] float? hold_ms = null)
    {
        await Connect();
        await Run("pad trigger", trigger, Math.Clamp(value, 0f, 1f), NonNegative(duration_ms));
        if (hold_ms != null)
        {
            await Run("wait", NonNegative(hold_ms.Value));
            await Run("pad trigger", trigger, 0, NonNegative(duration_ms));
        }
        return "ok";
    }

    [McpServerTool(Name = "game_profile")]
    [Description("Record every managed allocation's class, size and call stack for a number of updates (needs the client started with --profile, Linux), and return the report, per frame: the methods with the most bytes allocated under them, then the allocation sites by bytes.")]
    public static async Task<string> Profile(
        [Description("Updates to record over (1 to 3600).")] int frames = 120,
        [Description("Sites and methods to list (1 to 500).")] int top = 40)
    {
        if (frames is < 1 or > 3600 || top is < 1 or > 500)
        {
            throw new McpException("frames is 1 to 3600 and top 1 to 500");
        }
        string path = NextFile("profile", ".txt");
        await Run("profile", frames, top, path);
        return File.ReadAllText(path);
    }

    [McpServerTool(Name = "frame_stats", ReadOnly = true)]
    [Description("Measure the next frames drawn, like MangoHud's logging, and return JSON: fps with fps1Low and fps01Low (the rates of the 99th and 99.9th percentile frame times); budgetMs (one display refresh) and budgetUsed (the share the slower of CPU and GPU uses); frameMs (real time between frames, presenting included), cpuMs (main thread CPU time) and gpuMs (Linux; null where the driver can't time it); longFrames (over 1.5 budgets: hitches or missed vsyncs); and runtime (.NET): allocatedBytes (main thread) and gcPauseMs per frame, heapBytes, and in all, allThreadsAllocatedBytes (to within ~8 KB a thread), gcCollections by generation and jitMethods compiled. Distributions are mean, min, p50, p95, p99 and max. With path, also writes every frame to that CSV file, one row each.")]
    public static Task<string> FrameStats(
        [Description("Frames to measure, up to 100000.")] int frames = 120,
        [Description("A CSV file to write each frame's measurements to.")] string path = null) =>
        Run("frames", frames, path == null ? null : Path.GetFullPath(path));

    [McpServerTool(Name = "overlay")]
    [Description("Show or hide the profiler overlay (as Right Shift + F12 does); toggle it without shown. Returns whether it shows.")]
    public static Task<string> Overlay([Description("true to show, false to hide.")] bool? shown = null) =>
        Run("overlay", shown == null ? null : shown.Value ? "on" : "off");

    [McpServerTool(Name = "resize")]
    [Description("Resize the window, as dragging its edge does, and return its new size (WxH).")]
    public static Task<string> Resize(int width, int height) => Run("resize", width, height);

    [McpServerTool(Name = "game_wait", ReadOnly = true)]
    [Description("Let the game run for ms milliseconds or a number of updates.")]
    public static Task<string> Wait(float? ms = null, int? frames = null) =>
        frames != null ? Ok("wait frames", AtLeastOne(frames.Value)) : Ok("wait", NonNegative(ms ?? 0));

    [McpServerTool(Name = "release_all")]
    [Description("Release every synthetic button, key, stick and trigger.")]
    public static Task<string> ReleaseAll() => Ok("release");

    [McpServerTool(Name = "game_send")]
    [Description("Run raw control commands in order (see client/Terraria/Aether/Testing/ControlServer.cs); returns each result, stopping at an error, optionally with a screenshot at the end.")]
    public static async Task<CallToolResult> Send(
        [Description("Control commands, one per item, e.g. ['key press e', 'mouse drag 100 200 400 200 500', 'wait 250'].")] string[] commands,
        [Description("Also screenshot at the end, at this scale (0 to 1).")] float? screenshot_scale = null)
    {
        List<ContentBlock> results = new List<ContentBlock>();
        Stopwatch clock = Stopwatch.StartNew();
        foreach (string line in commands)
        {
            try
            {
                string result = await ControlServer.Execute(line);
                results.Add(new TextContentBlock { Text = (line + " -> ok " + result).TrimEnd() });
            }
            catch (McpException error)
            {
                results.Add(new TextContentBlock { Text = line + " -> error " + error.Message });
                break;
            }
        }
        results.Add(new TextContentBlock { Text = clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s" });
        if (screenshot_scale != null)
        {
            (string path, _) = await Shot(screenshot_scale.Value, null, null, 0);
            results.Add(ImageContentBlock.FromBytes(File.ReadAllBytes(path), "image/png"));
        }
        return new CallToolResult { Content = results };
    }

    private static async Task<(string Path, string Size)> Shot(float scale, int[] crop, string component, int padding)
    {
        string path = NextFile("shot", ".png");
        List<object> words = new List<object> { "screenshot" };
        if (component != null)
        {
            words.Add("component");
            words.Add(component);
            if (padding != 0)
            {
                words.Add("padding");
                words.Add(padding);
            }
        }
        if (crop != null)
        {
            words.Add("crop");
            foreach (int value in crop)
            {
                words.Add(value);
            }
        }
        words.Add("scale");
        words.Add(scale);
        words.Add(path);
        string result = await Run(words.ToArray());
        return (path, result.Split(' ')[0]);
    }

    private static async Task Connect()
    {
        if (!SyntheticInput.GamePadConnected)
        {
            await Run("pad connect");
        }
    }

    private static string NextFile(string name, string extension) =>
        Path.Combine(Scratch, $"{name}-{System.Threading.Interlocked.Increment(ref _files):0000}{extension}");

    private static async Task<string> Ok(params object[] words)
    {
        string result = await Run(words);
        return result == "" ? "ok" : result;
    }

    // Joins words into a command line (numbers in the invariant culture, nulls left out).
    private static Task<string> Run(params object[] words)
    {
        List<string> parts = new List<string>();
        foreach (object word in words)
        {
            if (word != null)
            {
                parts.Add(word is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : word.ToString());
            }
        }
        return ControlServer.Execute(string.Join(" ", parts));
    }

    private static float NonNegative(float milliseconds) =>
        milliseconds >= 0f ? milliseconds : throw new McpException("durations are 0 or more milliseconds");

    private static int AtLeastOne(int frames) => frames >= 1 ? frames : throw new McpException("frame counts are 1 or more");

    // For `type`: backslashes, and line breaks and tabs as \n and \t.
    private static string EscapeText(string text) =>
        text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\n", "\\n").Replace("\r", "\\n").Replace("\t", "\\t");
}
