using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.UI;
using Terraria.UI.Chat;

namespace Aether.UI.Game;

/// <summary>A line of text at the element's position: bordered (like the inventory's labels) or shadowed with chat tags.</summary>
public class UIGameLabel : UIElement
{
    public Func<string> Text;

    public Func<Color> Color = () => Microsoft.Xna.Framework.Color.White;

    /// <summary>Draw with ChatManager (shadowed, parses [c/...] tags) instead of a four-way border.</summary>
    public bool ColorCoded;

    private readonly ChatText _chatText = new ChatText();

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        CalculatedStyle dimensions = GetDimensions();
        string text = Text();
        if (ColorCoded)
        {
            _chatText.DrawWithShadow(spriteBatch, FontAssets.MouseText.Value, text, dimensions.Position(), Color(), 0f, Vector2.Zero, Vector2.One);
        }
        else
        {
            Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, text, dimensions.X, dimensions.Y, Color(), Microsoft.Xna.Framework.Color.Black, Vector2.Zero);
        }
    }
}

/// <summary>The coins in the player's piggy bank, safe, forge and void vault (ItemSlot.DrawSavings), drawn from the element's position.</summary>
public class UISavings : UIElement
{
    public bool Horizontal;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        CalculatedStyle dimensions = GetDimensions();
        ItemSlot.DrawSavings(spriteBatch, dimensions.X, dimensions.Y, Horizontal);
    }
}

/// <summary>The shop's price adjustment from the NPC's happiness: a face (happy to angry) and the percentage.</summary>
public class UIShopHappiness : UIElement
{
    private float _shownAdjustment = float.NaN;

    private string _percentage;

    public static float PriceAdjustment => Main.LocalPlayer.currentShoppingSettings.PriceAdjustment;

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        CalculatedStyle dimensions = GetDimensions();
        float priceAdjustment = PriceAdjustment;
        Texture2D texture = TextureAssets.NPCHappiness.Value;
        int frame = priceAdjustment <= 0.82f ? 0 : priceAdjustment <= 1f ? 1 : priceAdjustment <= 1.1f ? 2 : 3;
        Rectangle source = texture.Frame(4, 1, frame);
        spriteBatch.Draw(texture, new Vector2(dimensions.X + Main.shopHappinessIconOffsetX, dimensions.Y + Main.shopHappinessIconOffsetY), source, Microsoft.Xna.Framework.Color.White, 0f, source.Size() / 2f, 1f, SpriteEffects.None, 0f);
        if (priceAdjustment != _shownAdjustment)
        {
            _shownAdjustment = priceAdjustment;
            _percentage = priceAdjustment.ToString("P0");
        }
        Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, _percentage, dimensions.X + Main.shopHappinessTextOffsetX, dimensions.Y + Main.shopHappinessTextOffsetY, Microsoft.Xna.Framework.Color.White * ((float)(int)Main.mouseTextColor / 255f), Microsoft.Xna.Framework.Color.Black, Vector2.Zero);
    }
}

/// <summary>
/// The shop's header above its slots (Main.DrawInventory, outside the grid layout): the "Shop" label,
/// the player's savings and the happiness price adjustment, all anchored at (504, invBottom).
/// </summary>
public class UIShopHeader : UIGameScreen
{
    private const int X = 504;

    public static readonly UIShopHeader Instance = new UIShopHeader();

    public override void OnInitialize()
    {
        Append(new UIGameLabel
        {
            Text = () => Lang.inter[28].Value,
            Color = () => Color.White * ((float)(int)Main.mouseTextColor / 255f)
        });
        Append(new UISavings());
        Append(new UIShopHappiness());
    }

    /// <summary>Called by Main.DrawInventory in place of the header's legacy drawing; false: draw that.</summary>
    public static bool Draw()
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void UpdateState()
    {
        foreach (UIElement element in Elements)
        {
            element.Left.Set(X, 0f);
            element.Top.Set(Main.instance.invBottom, 0f);
        }
        Recalculate();
    }
}

/// <summary>
/// The reforge slot's caption (Main.DrawInventory): with an item in the slot, "Cost:", the price in
/// coloured coins and the player's savings; without, the "place an item here" hint.
/// </summary>
public class UIReforgeCost : UIGameScreen
{
    public static readonly UIReforgeCost Instance = new UIReforgeCost();

    private UIGameLabel _caption;

    private UIGameLabel _price;

    private UISavings _savings;

    private int _x;

    private int _y;

    private static bool HasItem => Main.reforgeItem.type > 0;

    // "Cost: " and the price in coins, made again only when the language or the price changes.
    private string _costLabel;

    private string _costLabelSource;

    private string _priceText;

    private long _pricedAt = -1;

    private GameCulture _pricedIn;

    private string CostLabel
    {
        get
        {
            string cost = Lang.inter[46].Value;
            if ((object)cost != _costLabelSource)
            {
                _costLabelSource = cost;
                _costLabel = cost + ": ";
            }
            return _costLabel;
        }
    }

    private string PriceText
    {
        get
        {
            long price = Price();
            if (price != _pricedAt || Language.ActiveCulture != _pricedIn)
            {
                _pricedAt = price;
                _pricedIn = Language.ActiveCulture;
                _priceText = CoinText(price);
            }
            return _priceText;
        }
    }

    public override void OnInitialize()
    {
        _caption = new UIGameLabel
        {
            ColorCoded = true,
            Text = () => HasItem ? CostLabel : Lang.inter[20].Value,
            Color = () => new Color(Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor, Main.mouseTextColor)
        };
        _price = new UIGameLabel
        {
            ColorCoded = true,
            Text = () => PriceText
        };
        _savings = new UISavings
        {
            Horizontal = true
        };
        Append(_savings);
        Append(_price);
        Append(_caption);
    }

    /// <summary>The reforge price of the slot's item, as Main.DrawInventory works it out.</summary>
    public static long Price()
    {
        Item item = Main.reforgeItem;
        long price = (long)item.value * (long)item.stack;
        if (Main.LocalPlayer.discountAvailable)
        {
            price = (long)((double)price * 0.8);
        }
        price = (long)((float)price * Main.LocalPlayer.currentShoppingSettings.PriceAdjustment);
        return price / 3;
    }

    /// <summary>A price as chat text, each coin in its colour (at least 1 copper), e.g. "[c/..:3 gold] ".</summary>
    public static string CoinText(long price)
    {
        int[] coins = CachedText.CoinsSplit(Math.Max(1L, price));
        string text = "";
        AddCoins(ref text, coins[3], Colors.CoinPlatinum, 15);
        AddCoins(ref text, coins[2], Colors.CoinGold, 16);
        AddCoins(ref text, coins[1], Colors.CoinSilver, 17);
        AddCoins(ref text, coins[0], Colors.CoinCopper, 18);
        return text;
    }

    private static void AddCoins(ref string text, long count, Color color, int name)
    {
        if (count > 0)
        {
            text = text + "[c/" + Colors.AlphaDarken(color).Hex3() + ":" + count + " " + Lang.inter[name].Value + "] ";
        }
    }

    /// <summary>Called by Main.DrawInventory in place of the caption's legacy drawing (x, y: the reforge slot); false: draw that.</summary>
    public static bool Draw(int x, int y)
    {
        if (!UIInventorySlots.Enabled)
        {
            return false;
        }
        Instance._x = x;
        Instance._y = y;
        GameScreens.Draw(Instance, Main.spriteBatch);
        return true;
    }

    public override void UpdateState()
    {
        bool hasItem = HasItem;
        _caption.Left.Set(_x + 50, 0f);
        _caption.Top.Set(_y, 0f);
        _price.Left.Set((float)(_x + 50) + FontAssets.MouseText.Value.MeasureString(CostLabel).X, 0f);
        _price.Top.Set(_y, 0f);
        _savings.Left.Set(_x + 130, 0f);
        _savings.Top.Set(Main.instance.invBottom, 0f);
        if (hasItem != (_price.Parent != null))
        {
            if (hasItem)
            {
                Append(_savings);
                Append(_price);
            }
            else
            {
                RemoveChild(_savings);
                RemoveChild(_price);
            }
        }
        Recalculate();
    }
}
