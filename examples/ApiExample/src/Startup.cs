using Aether.Api;
using Aether.Modding;
using Microsoft.Xna.Framework;
using Terraria.DataStructures;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

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
        var pulse = ProjectileTypeKey.Custom("api-example:pulse");
        ProjectileRegistry.Register("api-example:pulse", new ProjectileDefinition("Scout Pulse",
            EntitySprite.Vanilla(ProjectileID.PurpleLaser))
        {
            Targets = ProjectileTargets.Players,
            Lifetime = 120
        });
        var scout = NpcTypeKey.Custom("api-example:scout");
        NpcRegistry.Register("api-example:scout", new NpcDefinition("Scout", EntitySprite.Vanilla(NPCID.BlueSlime))
        {
            HasGravity = false,
            Damage = 6,
            MaxHealth = 40,
            OnTickStart = context =>
            {
                if (Players.TryGetLocal(out var player))
                {
                    Vector2 offset = player.Center - context.Npc.Center;
                    if (offset.LengthSquared() <= 1)
                        return;
                    Vector2 direction = Vector2.Normalize(offset);
                    Entities.TrySetNpcVelocity(context.Npc.Handle, direction * 2f);
                    int elapsed = context.State.Value is int count ? count + 1 : 1;
                    context.State.Value = elapsed % 90;
                    if (elapsed == 90)
                    {
                        var source = new EntitySource_Parent(Terraria.Main.npc[context.Npc.Handle.Slot]);
                        Entities.TrySpawnProjectile(pulse, new ProjectileSpawn(context.Npc.Center, direction * 6f,
                            8, 1f, Terraria.Main.maxPlayers, source), out _);
                    }
                }
            },
            OnRemoved = context => Console.WriteLine($"[api-example] Scout removed ({context.Reason})")
        });
        LocalCommands.Register("api-example:spawn-npc", arguments =>
        {
            if (Players.TryGetLocal(out var player))
                Entities.TrySpawnNpc(scout, new NpcSpawn(player.Bottom + new Vector2(64, -48),
                    new EntitySource_Parent(player)), out _);
        });
        HudOverlays.Register("api-example:label", canvas => canvas.DrawText("API example", 16, 80, HudColor.White));
        RetainedUiLayer counter = RetainedUi.Register("api-example:counter", CreateCounter, initiallyVisible: false);
        Keybinds.Register("api-example:counter", "F7", () => counter.IsVisible = !counter.IsVisible);
        RecipeRegistry.Register("api-example:torch", new RecipeDefinition(new ItemAmount(8, 1), [new ItemAmount(9, 1)]));
    }

    private static UIState CreateCounter()
    {
        var state = new UIState();
        var button = new UITextPanel<string>("Clicks: 0") { VAlign = 0.5f };
        button.Left.Set(16, 0);
        button.Width.Set(160, 0);
        button.Height.Set(44, 0);
        int clicks = 0;
        button.OnLeftClick += (_, _) => button.SetText($"Clicks: {++clicks}");
        state.Append(button);
        return state;
    }
}
