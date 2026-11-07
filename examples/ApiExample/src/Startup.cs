using Aether.Api;
using Aether.Modding;
using Terraria.ID;

namespace ApiExample;

public sealed class Startup : IModInitializer
{
    public void Initialize()
    {
        PlayerModifier training = Players.RegisterModifier("api-example:training");
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
        LocalCommands.Register("api-example:swiftness", _ =>
        {
            if (Players.TryGetLocal(out var player) && Terraria.Main.netMode == 0 && Players.GetStats(player).IsAlive)
                Players.TryApplyEffect(player, BuffID.Swiftness, 600);
        });
        LocalCommands.Register("api-example:training", option =>
        {
            if (!Players.TryGetLocal(out var player) || Terraria.Main.netMode != 0 || !Players.GetStats(player).IsAlive)
                return;
            if (option == "off")
                training.Clear(player);
            else
                training.Set(player, new PlayerStatModifier { DefenseBonus = 5, MeleeDamageMultiplier = 1.25f });
        });
        LocalCommands.Register("api-example:inventory", _ =>
        {
            if (Players.TryGetLocal(out var player))
            {
                int occupied = Players.GetInventory(player).Slots.Count(slot => slot.CanRead && slot.GetItem() != null);
                Console.WriteLine($"[api-example] {occupied} occupied player slots");
            }
        });
        LocalCommands.Register("api-example:spawn", _ =>
        {
            if (Worlds.TryGetCurrent(out var world))
                Console.WriteLine($"[api-example] {world.Name} spawn {world.Spawn}, tile {Tiles.Query(world.Spawn).Status}");
        });
        HudOverlays.Register("api-example:label", canvas => canvas.DrawText("API example", 16, 80, HudColor.White));
        RecipeRegistry.Register("api-example:torch", new RecipeDefinition(new ItemAmount(8, 1), [new ItemAmount(9, 1)]));
    }
}
