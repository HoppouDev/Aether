using System.Text.Json;
using Aether.Api;
using Aether.Modding;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace CustomItems;

public sealed class Startup : IModInitializer
{
    private const string Blade = "custom-items:blade";
    private const string Charm = "custom-items:charm";

    public void Initialize()
    {
        ItemRegistry.Register(Blade, new ItemDefinition("Counter Blade", ItemID.IronBroadsword)
        {
            Damage = 15,
            Knockback = 4,
            Tooltip = "Tracks swings and hits per blade.",
            OnUse = context => Update(context.Item, hit: false),
            OnHitNpc = context => Update(context.Item, hit: true)
        });
        ItemRegistry.Register(Charm, new ItemDefinition("Guard Charm", ItemID.Aglet)
        {
            Accessory = true,
            Tooltip = "+1 defense while equipped.",
            OnEquippedTick = context => context.Player.statDefense += 1
        });
        ClientCommands.Register("custom-items:give", command => command.Executes(context =>
        {
            var source = new EntitySource_DebugCommand();
            context.Source.Player.QuickSpawnItem(source, ItemRegistry.Create(ItemKey.Custom(Blade)));
            context.Source.Player.QuickSpawnItem(source, ItemRegistry.Create(ItemKey.Custom(Charm)));
            return 1;
        }));
        ClientCommands.Register("custom-items:inspect", command => command.Executes(context =>
        {
            Item item = context.Source.Player.HeldItem;
            if (ItemRegistry.GetId(item.type) != Blade)
            {
                context.Source.ReplyError("Hold a Counter Blade first.");
                return 0;
            }
            var (swings, hits) = Read(item);
            string text = $"Counter Blade has {swings} swings and {hits} hits.";
            context.Source.Reply(text);
            Console.WriteLine(text);
            return 1;
        }));
    }

    private static (int Swings, int Hits) Read(Item item)
    {
        return ItemRegistry.GetState(item) is { } state
            ? (state.GetProperty("swings").GetInt32(), state.GetProperty("hits").GetInt32())
            : (0, 0);
    }

    private static void Update(Item item, bool hit)
    {
        var (swings, hits) = Read(item);
        ItemRegistry.SetState(item, JsonSerializer.SerializeToElement(new
        {
            swings = hit ? swings : swings + 1,
            hits = hit ? hits + 1 : hits
        }));
    }
}
