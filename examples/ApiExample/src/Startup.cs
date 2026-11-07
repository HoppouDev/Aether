using Aether.Api;
using Aether.Modding;

namespace ApiExample;

public sealed class Startup : IModInitializer
{
    public void Initialize()
    {
        ClientEvents.Ready.Register(_ => Console.WriteLine("[api-example] Client ready"));
        WorldEvents.Entered.Register(world => Console.WriteLine($"[api-example] Entered {world.Name}"));
        WorldEvents.Left.Register(world => Console.WriteLine($"[api-example] Left {world.Name}"));
        Keybinds.Register("api-example:hello", "F6", () => Console.WriteLine("[api-example] Hello"));
        LocalCommands.Register("api-example:echo", text => Console.WriteLine($"[api-example] {text}"));
        LocalCommands.Register("api-example:player", _ =>
        {
            if (!Players.TryGetLocal(out var player))
                return;
            PlayerStats stats = Players.GetStats(player);
            Console.WriteLine($"[api-example] {stats.Name} ({stats.GameMode}) has {stats.Health}/{stats.MaxHealth} health and {stats.Mana}/{stats.MaxMana} mana");
        });
        HudOverlays.Register("api-example:label", canvas => canvas.DrawText("API example", 16, 80, HudColor.White));
        RecipeRegistry.Register("api-example:torch", new RecipeDefinition(new ItemAmount(8, 1), [new ItemAmount(9, 1)]));
    }
}
