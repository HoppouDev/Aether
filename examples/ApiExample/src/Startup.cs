using Aether.Api;
using Aether.Modding;
using Brigadier.NET;
using Brigadier.NET.Builder;
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
        ClientCommands.Register("api-example:echo", command => command
            .Then(c => c.Argument("text", Arguments.GreedyString())
                .Executes(context =>
                {
                    context.Source.Reply(Arguments.GetString(context, "text"));
                    return 1;
                })));
        ClientCommands.Register("api-example:player", command => command.Executes(context =>
        {
            PlayerStats stats = Players.GetStats(context.Source.Player);
            context.Source.Reply($"{stats.Name} ({stats.GameMode}) has {stats.Health}/{stats.MaxHealth} health and {stats.Mana}/{stats.MaxMana} mana");
            return 1;
        }));
        ClientCommands.Register("api-example:swiftness", command => command
            .Requires(source => Terraria.Main.netMode == 0)
            .Executes(context => Players.GetStats(context.Source.Player).IsAlive &&
                Players.TryApplyEffect(context.Source.Player, BuffID.Swiftness, 600) ? 1 : 0));
        ClientCommands.Register("api-example:training", command => command
            .Requires(source => Terraria.Main.netMode == 0)
            .Then(c => c.Literal("on").Executes(context =>
            {
                training.Set(context.Source.Player, new PlayerStatModifier { DefenseBonus = 5, MeleeDamageMultiplier = 1.25f });
                return 1;
            }))
            .Then(c => c.Literal("off").Executes(context =>
            {
                training.Clear(context.Source.Player);
                return 1;
            })));
        ClientCommands.Register("api-example:inventory", command => command.Executes(context =>
        {
            int occupied = Players.GetInventory(context.Source.Player).Slots.Count(slot => slot.CanRead && slot.GetItem() != null);
            context.Source.Reply($"{occupied} occupied player slots");
            return occupied;
        }));
        ClientCommands.Register("api-example:spawn", command => command.Executes(context =>
        {
            if (!Worlds.TryGetCurrent(out var world))
                return 0;
            context.Source.Reply($"{world.Name} spawn {world.Spawn}, tile {Tiles.Query(world.Spawn).Status}");
            return 1;
        }));
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
        ServerCommands.Register("api-example:spawn-npc", command => command.Executes(context =>
        {
            Terraria.Player player = context.Source.Player;
            if (!Entities.TrySpawnNpc(scout, new NpcSpawn(player.Bottom + new Vector2(64, -48),
                new EntitySource_Parent(player)), out _))
            {
                context.Source.ReplyError("Could not spawn a Scout here.");
                return 0;
            }
            context.Source.Reply("Spawned a Scout.");
            return 1;
        }));
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
