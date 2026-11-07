using System.ComponentModel;
using Aether.Modding;
using Terraria;
using Terraria.IO;
using Terraria.Testing;

namespace Aether.Api;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ApiStartup : IModInitializer
{
    public void Initialize()
    {
        Registration.SetGameThread();
        ClientStartup.HandleLocalCommand = LocalCommands.TryExecute;
        ClientStartup.BlockCraft = recipe => RecipeRegistry.IsBlocked((Recipe)recipe);
        ClientContent.FinalizeRegistration = () =>
        {
            ItemRegistry.Freeze();
            NpcRegistry.Freeze();
            ProjectileRegistry.Freeze();
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
        (Main.dedServ || Main.CanUpdateGameplay) && (Main.netMode != 1 || !Netplay.Disconnect);

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
            !Main.editSign && !Main.editChest && !Main.ingameOptionsWindow && !Main.InGameUI.IsVisible);
    }

    public static void Tick()
    {
        UpdateWorld();
        if (activeWorld != null && RecordReplay.Mode != RecordReplay.ReplayMode.Replaying &&
            !WorldUpdateStepper.Paused)
            WorldEvents.Tick.Raise(world);
    }

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

    public static void FilterGuideRecipes()
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

    [Inject("DrawInterface", At.RETURN)]
    private static void DrawHud(CallbackInfo callback) => GameBridge.DrawHud();

    [Inject("Main_Exiting(System.Object,System.EventArgs)", At.HEAD)]
    private static void Exit(CallbackInfo callback) => GameBridge.EndWorld();
}

[Mixin(typeof(Recipe))]
internal static class RecipeHooks
{
    [Inject("SetupRecipes()", At.RETURN, Allow = 1)]
    private static void Install(CallbackInfo callback) => GameBridge.InstallRecipes();

    [Inject("PlayerMeetsEnvironmentConditions", At.HEAD, Cancellable = true)]
    private static void Filter(Recipe self, Player player, List<string> missingObjects, CallbackInfoReturnable<bool> callback)
    {
        if (GameBridge.IsRecipeBlocked(self))
            callback.SetReturnValue(false);
    }

    [Inject("CollectGuideRecipes()", At.RETURN)]
    private static void FilterGuide(CallbackInfo callback) => GameBridge.FilterGuideRecipes();
}
