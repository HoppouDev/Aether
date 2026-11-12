using Aether.Modding;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.UI.Chat;

namespace Aether.Api;

public static class Keybinds
{
    private static readonly Dictionary<string, (Keys DefaultKey, Listener<int> Callback)> entries = [];
    private static readonly HashSet<Keys> previous = [];
    private static Dictionary<string, string> bindings = new(StringComparer.Ordinal);
    // Each registered keybind's key, resolved from the bindings file once it's loaded.
    private static (Keys Key, Listener<int> Callback)[] active = [];

    public static void Register(string id, string defaultKey, Action onPressed)
    {
        ArgumentNullException.ThrowIfNull(onPressed);
        if (!TryKey(defaultKey, out Keys key))
            throw new ArgumentException($"Unknown keyboard key '{defaultKey}'.", nameof(defaultKey));
        string owner = Registration.Owner();
        Registration.Add(entries, id, (key, new Listener<int>(owner, _ => onPressed())), owner);
    }

    private static bool TryKey(string? text, out Keys key) =>
        Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Keys.None &&
        string.Equals(key.ToString(), text, StringComparison.OrdinalIgnoreCase);

    internal static void Load(string savePath)
    {
        try
        {
            LoadBindings(savePath);
        }
        finally
        {
            active = entries.Select(entry => (TryKey(bindings.GetValueOrDefault(entry.Key), out Keys configured)
                ? configured : entry.Value.DefaultKey, entry.Value.Callback)).ToArray();
        }
    }

    private static void LoadBindings(string savePath)
    {
        string path = Path.Combine(savePath, "aether-api-keybinds.json");
        if (File.Exists(path))
        {
            try { bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []; }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                AetherLog.For("Aether.Api").Warning("Could not read '{Path}': {Reason}", path, exception.Message);
                return;
            }
        }

        bool changed = !File.Exists(path);
        foreach (var (id, entry) in entries)
            changed |= bindings.TryAdd(id, entry.DefaultKey.ToString());
        if (entries.Count == 0 || !changed)
            return;

        try
        {
            Directory.CreateDirectory(savePath);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(bindings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AetherLog.For("Aether.Api").Warning("Could not save '{Path}': {Reason}", path, exception.Message);
        }
    }

    internal static void Update(KeyboardState keyboard, bool acceptsInput)
    {
        foreach (var (key, callback) in active)
        {
            if (acceptsInput && keyboard.IsKeyDown(key) && !previous.Contains(key))
                callback.Invoke(0);
        }

        previous.Clear();
        foreach (var (key, _) in active)
        {
            if (keyboard.IsKeyDown(key))
                previous.Add(key);
        }
    }
}

public readonly record struct HudColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public static HudColor White { get; } = new(255, 255, 255);
}

public sealed class HudCanvas
{
    private readonly SpriteBatch batch;
    private bool active = true;
    private readonly string owner;

    internal HudCanvas(SpriteBatch batch, string owner, int width, int height)
    {
        this.batch = batch;
        this.owner = owner;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public void DrawText(string text, float x, float y, HudColor color)
    {
        EnsureActive();
        ChatManager.DrawColorCodedStringWithShadow(batch, FontAssets.MouseText.Value, text,
            new Vector2(x, y), ToColor(color), 0, Vector2.Zero, Vector2.One);
    }

    public void DrawTexture(string resourcePath, int x, int y, int width, int height, HudColor color)
    {
        EnsureActive();
        Texture2D texture = HudOverlays.Texture(owner, resourcePath);
        batch.Draw(texture, new Rectangle(x, y, width, height), ToColor(color));
    }

    private void EnsureActive()
    {
        if (!active)
            throw new InvalidOperationException("The HUD draw context has expired.");
    }

    internal void Expire() => active = false;

    private static Color ToColor(HudColor color) => new(color.Red, color.Green, color.Blue, color.Alpha);
}

public static class HudOverlays
{
    private static readonly Dictionary<string, Listener<HudCanvas>> entries = [];
    private static readonly Dictionary<(string, string), Texture2D> textures = [];

    public static void Register(string id, Action<HudCanvas> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        string owner = Registration.Owner();
        Registration.Add(entries, id, new Listener<HudCanvas>(owner, draw), owner);
    }

    internal static Texture2D Texture(string owner, string path)
    {
        if (textures.TryGetValue((owner, path), out var texture) && !texture.IsDisposed)
            return texture;
        using var source = Aether.Modding.ModResources.OpenRead(owner, path);
        return textures[(owner, path)] = Texture2D.FromStream(Main.instance.GraphicsDevice, source);
    }

    internal static void Draw(SpriteBatch batch, int width, int height, Matrix transform)
    {
        if (entries.Count == 0)
            return;
        batch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, transform);
        try
        {
            foreach (var overlay in entries.Values)
            {
                var canvas = new HudCanvas(batch, overlay.Owner, width, height);
                overlay.Invoke(canvas);
                canvas.Expire();
            }
        }
        finally
        {
            batch.End();
        }
    }

    internal static void ClearTextures()
    {
        foreach (var texture in textures.Values)
            texture.Dispose();
        textures.Clear();
    }
}
