using System;
using Aether.Api;
using Aether.Modding;

public sealed class ApiConsumer : IModInitializer
{
    public void Initialize() => Console.WriteLine(ApiMarker.Next());
}

[Mixin(typeof(Target))]
internal static class ApiHook
{
    [Inject("Entry(System.Int32)", At.HEAD)]
    private static void Call(CallbackInfoReturnable<int> callback) => Console.WriteLine(ApiMarker.Next());
}
