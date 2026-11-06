using Aether.Modding;

namespace ModProjectName;

public sealed class Startup : IModInitializer
{
    public const string ModId = "example-mod-id";

    public void Initialize()
    {
        using var reader = new StreamReader(ModResources.OpenRead(ModId, "welcome.txt"));
        Console.WriteLine($"[{ModId}] {reader.ReadLine()}");
    }
}
