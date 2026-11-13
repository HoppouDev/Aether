using System.ComponentModel;
using Aether.Modding;
using Terraria;
using Terraria.Chat;
using Terraria.IO;
using Terraria.Testing;

namespace Aether.Api;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ApiStartup : IModInitializer
{
    public void Initialize()
    {
        Registration.SetGameThread();
        ClientStartup.HandleLocalCommand = ClientCommands.TryExecute;
        ClientStartup.BlockCraft = recipe => RecipeRegistry.IsBlocked((Recipe)recipe);
        ClientContent.FinalizeRegistration = () =>
        {
            ItemRegistry.Freeze();
            NpcRegistry.Freeze();
            ProjectileRegistry.Freeze();
            ClientCommands.Registry.Freeze();
            ServerCommands.Registry.Freeze();
            AetherLog.For("Aether.Api").Information(
                "Registered {Items} items, {Npcs} NPCs, {Projectiles} projectiles, {ClientCommands} client and {ServerCommands} server commands",
                ClientContent.Items.Count, ClientContent.Npcs.Count, ClientContent.Projectiles.Count,
                ClientCommands.Registry.Count, ServerCommands.Registry.Count);
        };
        ClientContent.ApplyPlayerModifiers = player => PlayerModifiers.Apply((Player)player);
        ClientContent.ServerReady = GameBridge.ReadyServer;
        ClientContent.ServerStopped = GameBridge.EndWorld;
        ClientContent.ServerPlayerJoined = index => ServerEvents.PlayerJoined.Raise(Main.player[index]);
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
public static class GameBridge
{
    private static bool ready;
    private static bool endingWorld;
    private static WorldFileData? activeWorld;
    private static WorldInfo world;

    internal static bool IsReady => ready;
    internal static long WorldRevision { get; private set; }

    internal static bool WorldReady => ready && !endingWorld && !Main.gameMenu && !WorldGen.isGeneratingOrLoadingWorld &&
        (Main.dedServ || Main.CanUpdateGameplay) && (Main.netMode == 0 || !Netplay.Disconnect);

    internal static bool AuthoritativeWorld => WorldReady &&
        (Main.netMode == 0 || Main.netMode == 2 && ServerEvents.IsDedicatedServer);

    internal static void ReadyServer()
    {
        ready = true;
        UpdateWorld();
        ServerEvents.Ready.Raise(EventArgs.Empty);
    }

    public static void Ready()
    {
        if (ready)
            return;
        ready = true;
        Keybinds.Load(Program.SavePath);
        ClientEvents.Ready.Raise(EventArgs.Empty);
    }

    public static void Update()
    {
        if (Main.dedServ)
            return;
        UpdateWorld();
        Keybinds.Update(Main.keyState, activeWorld != null && FocusHelper.AllowInputProcessing &&
            !Main.gamePaused && !Main.drawingPlayerChat && !Main.blockInput &&
            !Main.editSign && !Main.editChest && !Main.ingameOptionsWindow && !Main.InGameUI.IsVisible &&
            !Main.mapFullscreen && !Main.inFancyUI);
    }

    public static void Tick()
    {
        UpdateWorld();
        if (RaisesTicks)
            WorldEvents.TickEnd.Raise(world);
    }

    public static void BeforeWorldTick()
    {
        WorldFileData? previous = activeWorld;
        UpdateWorld();
        if (ReferenceEquals(previous, activeWorld) && RaisesTicks)
            WorldEvents.TickStart.Raise(world);
    }

    private static bool RaisesTicks => activeWorld != null && RecordReplay.Mode != RecordReplay.ReplayMode.Replaying &&
        !WorldUpdateStepper.Paused;

    private static void UpdateWorld()
    {
        WorldFileData? current = WorldReady ? Main.ActiveWorldFileData : null;
        if (current == activeWorld)
            return;
        EndWorld();
        activeWorld = current;
        if (current != null)
        {
            world = new WorldInfo(current.GetWorldName(), current.WorldId, Main.netMode != 0);
            NpcRegistry.PublishRestored();
            WorldEvents.Entered.Raise(world);
        }
    }

    public static void EndWorld()
    {
        if (activeWorld == null)
            return;
        activeWorld = null;
        WorldRevision++;
        endingWorld = true;
        try
        {
            ProjectileRegistry.EndWorld();
            NpcRegistry.EndWorld();
            PlayerModifiers.ClearWorld();
            RetainedUi.EndWorld();
            WorldEvents.Left.Raise(world);
            HudOverlays.ClearTextures();
        }
        finally
        {
            endingWorld = false;
        }
    }

    public static void DrawHud()
    {
        if (activeWorld != null)
            HudOverlays.Draw(Main.spriteBatch, (int)(Main.screenWidth / Main.UIScale),
                (int)(Main.screenHeight / Main.UIScale), Main.UIScaleMatrix);
    }

    public static void InstallRecipes() => RecipeRegistry.Install();

    public static float GetPlayerWeaponFactor(Player player, Item item) => PlayerModifiers.WeaponFactor(player, item);

    public static bool IsRecipeBlocked(Recipe recipe) => RecipeRegistry.IsBlocked(recipe);

    public static void NpcTickEnded(NPC npc) => NpcRegistry.OnTickEnd(npc);

    public static bool TryServerCommand(ChatMessage message, int clientId) => ServerCommands.TryExecute(message, clientId);

    public static void ProjectileTickEnded(Projectile projectile) => ProjectileRegistry.OnTickEnd(projectile);

    /// <summary>Removes blocked recipes from the available list (multiplayer only).</summary>
    public static void FilterAvailableRecipes()
    {
        if (Main.netMode == 0)
            return;
        int write = 0;
        for (int index = 0; index < Main.numAvailableRecipes; index++)
        {
            int recipe = Main.availableRecipe[index];
            if (!IsRecipeBlocked(Main.recipe[recipe]))
                Main.availableRecipe[write++] = recipe;
        }
        Main.numAvailableRecipes = write;
    }
}

[Mixin(typeof(Main))]
internal static class GameHooks
{
    [Inject("DrawMenu", At.RETURN)]
    private static void Ready(CallbackInfo callback) => GameBridge.Ready();

    [Inject("Update(Microsoft.Xna.Framework.GameTime)", At.RETURN)]
    private static void Update(CallbackInfo callback) => GameBridge.Update();

    [Inject("DoUpdateInWorld()", At.RETURN)]
    private static void Tick(CallbackInfo callback) => GameBridge.Tick();

    [Inject("DoUpdateInWorld()", At.HEAD)]
    private static void BeforeWorldTick(CallbackInfo callback) => GameBridge.BeforeWorldTick();

    [Inject("DrawInterface", At.RETURN)]
    private static void DrawHud(CallbackInfo callback) => GameBridge.DrawHud();

    [Inject("Main_Exiting(System.Object,System.EventArgs)", At.HEAD)]
    private static void Exit(CallbackInfo callback) => GameBridge.EndWorld();
}

[Mixin(typeof(NPC))]
internal static class NpcHooks
{
    [Inject("UpdateNPC(System.Int32)", At.RETURN)]
    private static void TickEnd(NPC self, int i, CallbackInfo callback) => GameBridge.NpcTickEnded(self);
}

[Mixin(typeof(Projectile))]
internal static class ProjectileHooks
{
    [Inject("Update(System.Int32)", At.RETURN)]
    private static void TickEnd(Projectile self, int i, CallbackInfo callback) => GameBridge.ProjectileTickEnded(self);
}

[Mixin(typeof(ChatCommandProcessor))]
internal static class ChatHooks
{
    [Inject("ProcessIncomingMessage(Terraria.Chat.ChatMessage,System.Int32)", At.HEAD, Cancellable = true)]
    private static void ServerCommand(ChatCommandProcessor self, ChatMessage message, int clientId, CallbackInfo callback)
    {
        if (GameBridge.TryServerCommand(message, clientId))
            callback.Cancel();
    }
}

[Mixin(typeof(Recipe))]
internal static class RecipeHooks
{
    [Inject("SetupRecipes()", At.INVOKE, Target = "Terraria.Recipe::CreateRequiredItemQuickLookups()", Allow = 1)]
    private static void Install(CallbackInfo callback) => GameBridge.InstallRecipes();

    // Once per list update, before the focus is restored: not a callback per recipe, since the list is
    // rebuilt every frame the inventory is open.
    [Inject("UpdateRecipeList()", At.INVOKE, Target = "Terraria.Recipe::TryRefocusingRecipe(System.Int32)", Ordinal = 1)]
    private static void Filter(CallbackInfo callback) => GameBridge.FilterAvailableRecipes();

    [Inject("CollectGuideRecipes()", At.RETURN)]
    private static void FilterGuide(CallbackInfo callback) => GameBridge.FilterAvailableRecipes();
}
