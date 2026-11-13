using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;

namespace Aether.Testing;

/// <summary>
/// A component tree besides Terraria's retained UI, such as a mod's own UI toolkit's, that remote UI
/// automation lists and acts on (<c>game_ui_layout</c>, <c>game_ui_action</c>,
/// <c>game_screenshot</c>'s component crops). Register it with <see cref="UiTrees.Register" />.
/// Everything here runs on the game's main thread, at the start of an update.
/// </summary>
public interface IUiTree
{
    /// <summary>
    /// The tree's name in <c>game_ui_layout</c>'s output (e.g. <c>gum</c>); its component IDs start
    /// with it and a colon (<c>gum:12</c>), which is how actions find their tree.
    /// </summary>
    string Id { get; }

    /// <summary>The mod that owns the tree, for the output (its manifest ID).</summary>
    string Owner { get; }

    /// <summary>
    /// The tree's components, parents before children, as JSON objects: <c>id</c> (with the tree's
    /// prefix), <c>parent</c>, <c>type</c>, <c>label</c>, <c>bounds</c> ([x, y, width, height] in
    /// back-buffer pixels), <c>value</c> and <c>actions</c> (any of <c>activate</c>, <c>secondary</c>,
    /// <c>set_value</c> (0 to 1) and <c>set_text</c>), plus whatever else describes them.
    /// </summary>
    IEnumerable<JsonObject> Components(bool interactiveOnly);

    /// <summary>A component's bounds in back-buffer pixels, if it's still there.</summary>
    bool TryGetBounds(string componentId, out Rectangle bounds);

    /// <summary>Acts on a component; false, with why, if it can't (gone, or no such action).</summary>
    bool TryAct(string componentId, string action, JsonNode value, out string error);
}

/// <summary>The component trees registered besides Terraria's retained UI (see <see cref="IUiTree" />).</summary>
public static class UiTrees
{
    /// <summary>The name of Terraria's retained UI's tree in <c>game_ui_layout</c>'s output.</summary>
    public const string Terraria = "terraria";

    private static readonly List<IUiTree> Registered = new List<IUiTree>();

    public static IReadOnlyList<IUiTree> All => Registered;

    /// <summary>Adds a tree; its ID must be unique and not <see cref="Terraria" />.</summary>
    public static void Register(IUiTree tree)
    {
        if (tree.Id == Terraria || tree.Id.Contains(':') || Find(tree.Id) != null)
        {
            throw new ArgumentException($"UI tree ID {tree.Id} is taken or invalid.", nameof(tree));
        }
        Registered.Add(tree);
    }

    /// <summary>The registered tree a component ID belongs to (by its prefix), or null for Terraria's.</summary>
    public static IUiTree ForComponent(string componentId)
    {
        int colon = componentId.IndexOf(':');
        return colon > 0 ? Find(componentId.Substring(0, colon)) : null;
    }

    private static IUiTree Find(string id)
    {
        foreach (IUiTree tree in Registered)
        {
            if (tree.Id == id)
            {
                return tree;
            }
        }
        return null;
    }
}
