using Aether.Modding;

namespace ModProjectName.Mixins;

[Mixin(typeof(Terraria.Program))]
public static class StartupMixin
{
    [Inject("RunGame()", At.HEAD, Require = 1, Allow = 1)]
    private static void OnRunGame(CallbackInfo callback) =>
        Console.WriteLine($"[{Startup.ModId}] RunGame entered");
}
