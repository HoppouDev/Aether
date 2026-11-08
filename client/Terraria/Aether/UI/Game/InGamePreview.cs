using System;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace Aether.UI.Game;

/// <summary>
/// Test controls for the migrated in-game UI, only when the game was started with
/// <c>-aetherscreens</c> (`./aether screens`). All are Ctrl+Shift chords:
/// <list type="bullet">
/// <item>L switches every migrated piece between its retained and legacy versions (for comparing them);</item>
/// <item>B opens the player's piggy bank (a container to test with, wherever you are);</item>
/// <item>G / S / R talk to the nearest town NPC and open the Guide's crafting slot / a shop (the
/// Merchant's stock) / the reforge slot (R again: fill the empty slot with a gold broadsword, or empty it);</item>
/// <item>U gives a few timed buffs (Well Fed, Ironskin, Regeneration, Swiftness);</item>
/// <item>I gives crafting materials (wood, gel, stone), a watch, depth meter and ruler, and savings
/// (coins in the piggy bank and safe);</item>
/// <item>N brings the nearest town NPC over and talks to it, and H does that and opens the stylist's hair window;</item>
/// <item>K adds banner kills (a claimable slime and zombie banner);</item>
/// <item>O places a dresser beside the player (in the world: use a test world) and opens its clothes window;</item>
/// <item>V cycles the life and mana style, and J sets 333/500 life (with life fruit) and 77/200 mana;</item>
/// <item>X kills the player, and E shows invasion progress (in turn: partway, a wave, cleared);</item>
/// <item>A shows an achievement unlocked popup (without unlocking it).</item>
/// </list>
/// </summary>
public static class InGamePreview
{
    private static TimeSpan _lastFrame = TimeSpan.MinValue;

    private static bool _legacyKeyWasDown;

    private static bool _bankKeyWasDown;

    private static bool _guideKeyWasDown;

    private static bool _shopKeyWasDown;

    private static bool _reforgeKeyWasDown;

    private static bool _styleKeyWasDown;

    private static bool _statsKeyWasDown;

    private static bool _deathKeyWasDown;

    private static bool _invasionKeyWasDown;

    private static int _invasionReports;

    private static bool _popupKeyWasDown;

    private static int _popups;

    private static bool _buffKeyWasDown;

    private static bool _itemKeyWasDown;

    private static bool _talkKeyWasDown;

    private static bool _hairKeyWasDown;

    private static bool _dresserKeyWasDown;

    private static bool _bannerKeyWasDown;

    public static bool IsEnabled => Program.LaunchParameters.ContainsKey("-aetherscreens");

    // Menus tied to an NPC close as soon as the player isn't talking to one.
    private static void AddToBank(Chest bank, int type, int stack)
    {
        for (int i = 0; i < bank.item.Length; i++)
        {
            if (bank.item[i].IsAir)
            {
                bank.item[i].SetDefaults(type);
                bank.item[i].stack = stack;
                return;
            }
        }
    }

    private static void TalkToNearestTownNPC()
    {
        Player player = Main.LocalPlayer;
        int nearest = -1;
        float distance = float.MaxValue;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && npc.townNPC && player.Distance(npc.Center) < distance)
            {
                distance = player.Distance(npc.Center);
                nearest = i;
            }
        }
        if (nearest >= 0)
        {
            player.SetTalkNPC(nearest);
        }
    }

    /// <summary>Called by the migrated pieces' draw hooks (whether or not they're showing legacy).</summary>
    public static void Update()
    {
        TimeSpan frame = Main._drawInterfaceGameTime.TotalGameTime;
        if (frame == _lastFrame || !IsEnabled)
        {
            return;
        }
        _lastFrame = frame;
        bool chord = (Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl)) && (Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift));
        bool legacyKey = chord && Main.keyState.IsKeyDown(Keys.L);
        bool bankKey = chord && Main.keyState.IsKeyDown(Keys.B);
        if (legacyKey && !_legacyKeyWasDown)
        {
            UIInventorySlots.Enabled = !UIInventorySlots.Enabled;
            UIIngameOptions.ShowLegacy = !UIInventorySlots.Enabled;
            SoundEngine.PlaySound(12);
            Main.NewText("Aether: " + (UIInventorySlots.Enabled ? "retained" : "legacy") + " in-game UI");
        }
        bool guideKey = chord && Main.keyState.IsKeyDown(Keys.G);
        bool shopKey = chord && Main.keyState.IsKeyDown(Keys.S);
        bool reforgeKey = chord && Main.keyState.IsKeyDown(Keys.R);
        if ((guideKey && !_guideKeyWasDown) || (shopKey && !_shopKeyWasDown) || (reforgeKey && !_reforgeKeyWasDown))
        {
            TalkToNearestTownNPC();
            if (reforgeKey)
            {
                // The Goblin Tinkerer's reforge slot, with any NPC; a second press empties or fills the slot.
                Main.playerInventory = true;
                Main.npcChatText = "";
                if (Main.InReforgeMenu && Main.reforgeItem.type > 0)
                {
                    Main.reforgeItem.TurnToAir();
                }
                else if (Main.InReforgeMenu)
                {
                    Main.reforgeItem.SetDefaults(ItemID.GoldBroadsword);
                }
                Main.InReforgeMenu = true;
                SoundEngine.PlaySound(12);
            }
            else if (guideKey)
            {
                Main.playerInventory = true;
                Main.npcChatText = "";
                Main.InGuideCraftMenu = true;
                SoundEngine.PlaySound(12);
            }
            else
            {
                Main.instance.OpenShop(1);
            }
        }
        bool styleKey = chord && Main.keyState.IsKeyDown(Keys.V);
        if (styleKey && !_styleKeyWasDown)
        {
            Main.ResourceSetsManager.CycleResourceSet();
            Main.NewText("Aether: life and mana style " + Main.ResourceSetsManager.ActiveSetKeyName);
        }
        _styleKeyWasDown = styleKey;
        bool statsKey = chord && Main.keyState.IsKeyDown(Keys.J);
        if (statsKey && !_statsKeyWasDown)
        {
            // Partly full life (with life fruit) and mana, for the resource displays.
            Main.LocalPlayer.statLifeMax = 500;
            Main.LocalPlayer.statManaMax = 200;
            Main.LocalPlayer.statLife = 333;
            Main.LocalPlayer.statMana = 77;
        }
        _statsKeyWasDown = statsKey;
        bool deathKey = chord && Main.keyState.IsKeyDown(Keys.X);
        if (deathKey && !_deathKeyWasDown && !Main.LocalPlayer.dead)
        {
            Main.LocalPlayer.KillMe(PlayerDeathReason.ByCustomReason("Aether test"), 9999.0, 0);
        }
        _deathKeyWasDown = deathKey;
        bool invasionKey = chord && Main.keyState.IsKeyDown(Keys.E);
        if (invasionKey && !_invasionKeyWasDown)
        {
            // Only the progress display: a third of the way, then wave 3 of a wave event, then cleared.
            switch (_invasionReports++ % 3)
            {
            case 0:
                Main.ReportInvasionProgress(30, 90, 1, 0);
                break;
            case 1:
                Main.ReportInvasionProgress(120, 200, 5, 3);
                break;
            default:
                Main.ReportInvasionProgress(90, 90, 1, 0);
                break;
            }
        }
        _invasionKeyWasDown = invasionKey;
        bool popupKey = chord && Main.keyState.IsKeyDown(Keys.A);
        if (popupKey && !_popupKeyWasDown)
        {
            // Only the popup: nothing is unlocked.
            string[] names = new string[3] { "TIMBER", "BENCHED", "OBTAIN_HAMMER" };
            InGameNotificationsTracker.AddCompleted(Main.Achievements.GetAchievement(names[_popups++ % names.Length]));
        }
        _popupKeyWasDown = popupKey;
        bool buffKey = chord && Main.keyState.IsKeyDown(Keys.U);
        if (buffKey && !_buffKeyWasDown)
        {
            foreach (int buff in new int[4] { 26, 5, 2, 3 })
            {
                Main.LocalPlayer.AddBuff(buff, 60 * 60 * 5);
            }
        }
        _buffKeyWasDown = buffKey;
        bool itemKey = chord && Main.keyState.IsKeyDown(Keys.I);
        if (itemKey && !_itemKeyWasDown)
        {
            Main.LocalPlayer.QuickSpawnItem(new EntitySource_DebugCommand(), 9, 99);
            Main.LocalPlayer.QuickSpawnItem(new EntitySource_DebugCommand(), 23, 30);
            Main.LocalPlayer.QuickSpawnItem(new EntitySource_DebugCommand(), 3, 99);
            // Savings (shown by shops and the reforge slot): 3 gold 45 silver in the piggy bank, a platinum in the safe.
            AddToBank(Main.LocalPlayer.bank, ItemID.GoldCoin, 3);
            AddToBank(Main.LocalPlayer.bank, ItemID.SilverCoin, 45);
            AddToBank(Main.LocalPlayer.bank2, ItemID.PlatinumCoin, 1);
            // Info and builder accessories, which work from the inventory: watch, depth meter, ruler.
            foreach (int item in new int[3] { 15, 18, 2799 })
            {
                Main.LocalPlayer.QuickSpawnItem(new EntitySource_DebugCommand(), item);
            }
        }
        _itemKeyWasDown = itemKey;
        bool talkKey = chord && Main.keyState.IsKeyDown(Keys.N);
        bool hairKey = chord && Main.keyState.IsKeyDown(Keys.H);
        if ((talkKey && !_talkKeyWasDown) || (hairKey && !_hairKeyWasDown))
        {
            TalkToNearestTownNPC();
            if (Main.LocalPlayer.talkNPC >= 0)
            {
                // Next to the player, so the conversation doesn't end at once for being out of range.
                NPC npc = Main.npc[Main.LocalPlayer.talkNPC];
                npc.position = Main.LocalPlayer.position + new Microsoft.Xna.Framework.Vector2(48f, 0f);
                npc.velocity = Microsoft.Xna.Framework.Vector2.Zero;
                Main.playerInventory = false;
                Main.npcChatText = Main.npc[Main.LocalPlayer.talkNPC].GetChat();
                SoundEngine.PlaySound(24);
                if (hairKey)
                {
                    Main.OpenHairWindow();
                }
            }
        }
        _talkKeyWasDown = talkKey;
        _hairKeyWasDown = hairKey;
        bool bannerKey = chord && Main.keyState.IsKeyDown(Keys.K);
        if (bannerKey && !_bannerKeyWasDown)
        {
            // Enough blue slime and zombie kills for a claimable banner of each (counted in the world).
            foreach (int npcType in new int[2] { 1, 3 })
            {
                for (int i = 0; i < 50; i++)
                {
                    BannerSystem.AddNPCKillBy(npcType, Main.myPlayer);
                }
            }
        }
        _bannerKeyWasDown = bannerKey;
        bool dresserKey = chord && Main.keyState.IsKeyDown(Keys.O);
        if (dresserKey && !_dresserKeyWasDown)
        {
            // The window stays open only next to a real dresser, so place one beside the player.
            Player player = Main.LocalPlayer;
            int y = (int)(player.Bottom.Y / 16f) - 1;
            int x = (int)(player.Center.X / 16f);
            foreach (int dx in new int[6] { 3, 4, 5, -3, -4, -5 })
            {
                // Clear the plants in the way, then try to place it.
                for (int i = x + dx - 1; i <= x + dx + 1; i++)
                {
                    for (int j = y - 1; j <= y; j++)
                    {
                        if (Main.tile[i, j].active() && !Main.tileSolid[Main.tile[i, j].type])
                        {
                            WorldGen.KillTile(i, j, fail: false, effectOnly: false, noItem: true);
                        }
                    }
                }
                if (WorldGen.PlaceTile(x + dx, y, 88))
                {
                    x += dx;
                    break;
                }
            }
            for (int i = x - 2; i <= x + 2; i++)
            {
                for (int j = y - 2; j <= y; j++)
                {
                    Tile tile = Main.tile[i, j];
                    if (tile.active() && tile.type == 88 && tile.frameX % 54 == 0 && tile.frameY == 0)
                    {
                        Main.interactedDresserTopLeftX = i;
                        Main.interactedDresserTopLeftY = j;
                        player.SetTalkNPC(-1);
                        Main.npcChatText = "";
                        Main.playerInventory = false;
                        Main.OpenClothesWindow();
                        _dresserKeyWasDown = true;
                        return;
                    }
                }
            }
        }
        _dresserKeyWasDown = dresserKey;
        _guideKeyWasDown = guideKey;
        _shopKeyWasDown = shopKey;
        _reforgeKeyWasDown = reforgeKey;
        if (bankKey && !_bankKeyWasDown)
        {
            Player player = Main.LocalPlayer;
            // The bank stays open while chestX/Y is in reach and solid: use the ground underfoot.
            player.chestX = (int)(player.Center.X / 16f);
            player.chestY = (int)(player.Bottom.Y / 16f);
            player.chest = -2;
            Main.playerInventory = true;
            SoundEngine.PlaySound(10);
        }
        _legacyKeyWasDown = legacyKey;
        _bankKeyWasDown = bankKey;
    }
}
