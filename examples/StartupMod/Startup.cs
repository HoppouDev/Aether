using Aether.Modding;

namespace Aether.Examples;

public sealed class Startup : IModInitializer
{
    public void Initialize() => StartupLog.Write("initializer ran");
}

[Mixin(typeof(Terraria.Program))]
public static class StartupMixin
{
    [Inject("RunGame()", At.HEAD)]
    private static void OnRunGame(CallbackInfo callback) => StartupLog.Write("RunGame entered");
}
