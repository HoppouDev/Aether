using System;
using System.Collections.Generic;
using Aether.UI.Menu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.Social;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace Aether.UI.Game;

/// <summary>
/// The in-game settings window (Esc / the Settings button; IngameOptions in vanilla): categories on
/// the left, the open category's options on the right. Same layout as IngameOptions.Draw, including
/// its per-language adjustments, and the same state (IngameOptions.category) and gamepad link points
/// (2900+ left, 2930+ right), so vanilla's gamepad page for it keeps working.
/// </summary>
public class UIIngameOptions : UIGameScreen
{
    private const float PanelWidth = 670f;

    private const float PanelHeight = 480f;

    private const float Margin = 20f;

    // Slider labels sit this far left of the column centre; sliders end at the panel's right edge.
    private const float SliderLabelShift = 70f;

    private const float BarScale = 0.75f;

    private static readonly UIIngameOptions Instance = new UIIngameOptions();

    // What the window was last built for: screen size, language, category, whether invites work.
    private bool _built;

    private int _builtWidth;

    private int _builtHeight;

    private GameCulture _builtCulture;

    private int _builtCategory;

    private bool _builtCanInvite;

    // Wrapped label texts (narrow layouts), by the label text they wrap.
    private readonly Dictionary<string, string> _wrapped = new Dictionary<string, string>();

    private Vector2 _origin;

    // Languages whose long texts get a narrower left column and smaller right text.
    private bool _narrow;

    private float _rightScale;

    private float _cursorBarShift;

    private float _wrapWidth;

    private bool _canInvite;

    private UIMenuSlider _uiScaleSlider;

    private readonly System.Collections.Generic.List<UIOptionsSliderRow> _rows = new System.Collections.Generic.List<UIOptionsSliderRow>();

    /// <summary>When set, IngameOptions draws its legacy immediate-mode window instead (for comparing them).</summary>
    public static bool ShowLegacy;

    /// <summary>
    /// Called by IngameOptions.Draw: draws the window and everything vanilla draws over it, and returns
    /// true, or false to let the legacy window draw instead.
    /// </summary>
    public static bool DrawWindow(SpriteBatch spriteBatch)
    {
        InGamePreview.Update();
        if (ShowLegacy)
        {
            GameScreens.Close(Instance);
            return false;
        }
        GameScreens.Draw(Instance, spriteBatch);
        Main.DrawInterface_29_SettingsButton();
        Main.DrawGamepadInstructions();
        Main.mouseText = false;
        Main.instance.GUIBarsDraw();
        Main.instance.DrawMouseOver();
        Main.spriteBatch.End();
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Aether.Graphics.PixelArt.Sampler(Main.SamplerStateForCursor), DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
        Main.DrawCursor(Main.DrawThickCursor());
        return true;
    }

    /// <summary>Called by IngameOptions.Open: the next draw starts fresh.</summary>
    public static void Opened()
    {
        GameScreens.Close(Instance);
    }

    private static bool IsActive(GameCulture.CultureName culture)
    {
        return GameCulture.FromCultureName(culture).IsActive;
    }

    private static bool CanInvite => SocialAPI.Network != null && SocialAPI.Network.CanInvite();

    private bool LayoutChanged()
    {
        if (!_built || _builtWidth != Main.screenWidth || _builtHeight != Main.screenHeight || _builtCulture != Language.ActiveCulture || _builtCategory != IngameOptions.category)
        {
            return true;
        }
        return _builtCanInvite != CanInvite;
    }

    public override void OnActivate()
    {
        _built = false;
    }

    public override void UpdateState()
    {
        if (LayoutChanged())
        {
            Build();
        }
        Rectangle panel = new Rectangle((int)(_origin.X - Margin), (int)(_origin.Y - Margin), (int)(PanelWidth + Margin * 2f), (int)(PanelHeight + Margin * 2f));
        IngameOptions._GUIHover = panel;
        if (panel.Contains(Main.MouseScreen.ToPoint()))
        {
            Main.player[Main.myPlayer].mouseInterface = true;
        }
        if (IngameOptions.category == 0)
        {
            if (Main.temporaryGUIScaleSlider == -1f)
            {
                Main.temporaryGUIScaleSlider = Main.UIScaleWanted;
            }
            // The UI scale applies when the slider is let go: changing it mid-drag moves the slider.
            if ((_uiScaleSlider == null || !_uiScaleSlider.IsDragging) && Main.temporaryGUIScaleSliderUpdate && Main.temporaryGUIScaleSlider != -1f)
            {
                Main.UIScale = Main.temporaryGUIScaleSlider;
                Main.temporaryGUIScaleSliderUpdate = false;
            }
        }
        if (IngameOptions.category == 3)
        {
            // The gamepad adjusts Main.hBar etc. (HandleOptionsSpecials); keep them on the sliders.
            Main.mouseColorSlider.ApplyToMainLegacyBars();
            // ...unless the gamepad isn't on a cursor colour bar, in which case the border's apply.
            bool cursorFocused = false;
            foreach (UIOptionsSliderRow row in _rows)
            {
                cursorFocused |= row.IsFocused && row.Tag == 25;
            }
            if (PlayerInput.UsingGamepad && !cursorFocused)
            {
                Main.mouseBorderColorSlider.ApplyToMainLegacyBars();
            }
            if (Main.mouseColorSlider.Luminance < 0.15f)
            {
                Main.mouseColorSlider.Luminance = 0.15f;
            }
            Main.mouseColor = Main.mouseColorSlider.GetColor();
            Main.MouseBorderColor = Main.mouseBorderColorSlider.GetColor();
        }
    }

    private void Build()
    {
        _built = true;
        _builtWidth = Main.screenWidth;
        _builtHeight = Main.screenHeight;
        _builtCulture = Language.ActiveCulture;
        _builtCategory = IngameOptions.category;
        _builtCanInvite = CanInvite;
        _wrapped.Clear();
        RemoveAllChildren();
        _rows.Clear();
        _uiScaleSlider = null;
        _narrow = IsActive(GameCulture.CultureName.Russian) || IsActive(GameCulture.CultureName.Portuguese) || IsActive(GameCulture.CultureName.Polish) || IsActive(GameCulture.CultureName.French);
        bool german = IsActive(GameCulture.CultureName.German);
        bool italianOrSpanish = IsActive(GameCulture.CultureName.Italian) || IsActive(GameCulture.CultureName.Spanish);
        _wrapWidth = IsActive(GameCulture.CultureName.Polish) ? 200f : 300f;
        _cursorBarShift = _narrow ? 126f : 60f;
        _canInvite = SocialAPI.Network != null && SocialAPI.Network.CanInvite();
        _origin = new Vector2(Main.screenWidth, Main.screenHeight) / 2f - new Vector2(PanelWidth, PanelHeight) / 2f;
        BuildLeft(_narrow ? 0.4f : (german ? 0.55f : 0.7f), _narrow ? 0.44f : (german ? 0.6f : 0.8f));
        int category = IngameOptions.category;
        _rightScale = 1f - (_narrow ? 0.1f : 0f) - ((german && category == 3) ? 0.15f : 0f) - ((italianOrSpanish && (category == 0 || category == 3)) ? 0.2f : 0f);
        switch (category)
        {
        case 0:
            BuildGeneral();
            break;
        case 1:
            BuildInterface();
            break;
        case 2:
            BuildVideo();
            break;
        case 3:
            BuildCursor();
            break;
        }
        Recalculate();
    }

    // --- left column ---------------------------------------------------------------------------

    private void BuildLeft(float minScale, float maxScale)
    {
        int count = 8 + (_canInvite ? 1 : 0);
        Vector2 anchor = new Vector2(_origin.X + PanelWidth / 4f - (_narrow ? 55f : 0f), _origin.Y + Margin * 5f / 2f);
        Vector2 step = new Vector2(0f, PanelHeight - Margin * 5f) / count;
        int index = 0;
        void Add(string text, Action onClick, int category = -1)
        {
            Vector2 center = anchor + step * (index + 1);
            Append(new UIOptionsCategory(text, onClick, (category >= 0) ? (Func<bool>)(() => IngameOptions.category == category) : null, center, minScale, maxScale));
            UILinkPointNavigator.SetPosition(2900 + index, center);
            index++;
        }
        void Category(int text, int category)
        {
            Add(Lang.menu[text].Value, delegate
            {
                IngameOptions.category = category;
                Terraria.Audio.SoundEngine.PlaySound(10);
            }, category);
        }
        Category(114, 0);
        Category(210, 1);
        Category(63, 2);
        Category(218, 3);
        Add(Lang.menu[66].Value, delegate
        {
            IngameOptions.Close();
            IngameFancyUI.OpenKeybinds();
        });
        if (_canInvite)
        {
            Add(Lang.menu[147].Value, delegate
            {
                IngameOptions.Close();
                SocialAPI.Network.OpenInviteInterface();
            });
        }
        Add(Lang.menu[131].Value, delegate
        {
            IngameOptions.Close();
            IngameFancyUI.OpenAchievements();
        });
        Add(Lang.menu[118].Value, delegate
        {
            IngameOptions.Close();
        });
        Add(Lang.inter[35].Value, delegate
        {
            IngameOptions.Close();
            Main.menuMode = 10;
            Main.gameMenu = true;
            WorldGen.SaveAndQuit();
        });
        UILinkPointNavigator.Shortcuts.INGAMEOPTIONS_BUTTONS_LEFT = count;
    }

    // --- right column --------------------------------------------------------------------------

    private Vector2 _rightAnchor;

    private Vector2 _rightStep;

    private int _rightSlots;

    private int _gamepadPoints;

    private void BeginRight(int slots, float stepAdjust = 0f)
    {
        _rightSlots = slots;
        _gamepadPoints = 0;
        _rightAnchor = new Vector2(_narrow ? (_origin.X + PanelWidth * 2f / 3f) : (_origin.X + PanelWidth * 3f / 4f), _origin.Y + Margin * 5f / 2f);
        _rightStep = new Vector2(0f, PanelHeight - Margin * 3f) / (slots + 1);
        _rightStep.Y += stepAdjust;
        UILinkPointNavigator.Shortcuts.INGAMEOPTIONS_BUTTONS_RIGHT = 0;
    }

    private Vector2 Row(int slot)
    {
        return _rightAnchor + _rightStep * (slot + 1);
    }

    // Like the legacy loop: every non-header slot before _rightSlots gets a gamepad point at the column centre.
    private void LinkSlot(int slot)
    {
        if (slot < _rightSlots)
        {
            UILinkPointNavigator.SetPosition(2930 + _gamepadPoints, Row(slot) + new Vector2(_narrow ? -40f : 0f, 0f));
            _gamepadPoints++;
            UILinkPointNavigator.Shortcuts.INGAMEOPTIONS_BUTTONS_RIGHT = _gamepadPoints;
        }
    }

    private void Header(int slot, Func<string> text)
    {
        Append(new UIOptionsText(text, null, Row(slot), _rightScale)
        {
            IsHeader = true
        });
    }

    private void Toggle(int slot, Func<string> text, Action change, float scale = 1f)
    {
        Append(new UIOptionsText(text, change, Row(slot), _rightScale * scale));
        LinkSlot(slot);
    }

    private string Wrapped(string text)
    {
        if (!_narrow)
        {
            return text;
        }
        if (_wrapped.Count >= 256)
        {
            _wrapped.Clear();
        }
        if (!_wrapped.TryGetValue(text, out var wrapped))
        {
            wrapped = FontAssets.ItemStack.Value.CreateWrappedText(text, _wrapWidth, Language.ActiveCulture.CultureInfo);
            _wrapped[text] = wrapped;
        }
        return wrapped;
    }

    private UIMenuSlider Slider(int slot, Func<string> label, Func<float> getValue, Action<float> setValue, int gamepadFeature, float labelScale = 1f, float barShift = 0f, Utils.ColorLerpMethod colorMethod = null, int tag = 0)
    {
        Vector2 row = Row(slot);
        float barRight = _origin.X + PanelWidth - Margin / 2f - 20f - barShift;
        float rightColumnLeft = _narrow ? (_origin.X + PanelWidth / 3f + Margin) : (_origin.X + PanelWidth / 2f + Margin);
        UIOptionsSliderRow rowElement = new UIOptionsSliderRow(tag);
        rowElement.Left.Set(rightColumnLeft, 0f);
        rowElement.Top.Set(row.Y - _rightStep.Y / 2f, 0f);
        rowElement.Width.Set(barRight + 4f - rightColumnLeft, 0f);
        rowElement.Height.Set(_rightStep.Y, 0f);
        Append(rowElement);
        UIMenuSlider slider = new UIMenuSlider(getValue, setValue, gamepadFeature, BarScale, colorMethod);
        slider.Left.Set(barRight - slider.Width.Pixels - rightColumnLeft, 0f);
        slider.Top.Set(row.Y - 3f - slider.Height.Pixels / 2f - (row.Y - _rightStep.Y / 2f), 0f);
        slider.AutomationCaption = label;
        slider.AlsoFocused = () => rowElement.IsMouseHovering;
        rowElement.Append(slider);
        rowElement.Slider = slider;
        UIOptionsText text = new UIOptionsText(label, null, row - new Vector2(SliderLabelShift, 0f), _rightScale * labelScale)
        {
            AlsoLit = () => rowElement.IsFocused
        };
        rowElement.Append(text);
        _rows.Add(rowElement);
        LinkSlot(slot);
        return slider;
    }

    private void BuildGeneral()
    {
        BeginRight(17);
        Header(0, () => Lang.menu[65].Value);
        Slider(1, () => CachedText.Concat(Lang.menu[99].Value, " ", CachedText.Number(Math.Round(Main.musicVolume * 100f)), "%"), () => Main.musicVolume, delegate(float v)
        {
            Main.musicVolume = v;
        }, 2);
        Slider(2, () => CachedText.Concat(Lang.menu[98].Value, " ", CachedText.Number(Math.Round(Main.soundVolume * 100f)), "%"), () => Main.soundVolume, delegate(float v)
        {
            Main.soundVolume = v;
        }, 3);
        Slider(3, () => CachedText.Concat(Lang.menu[119].Value, " ", CachedText.Number(Math.Round(Main.ambientVolume * 100f)), "%"), () => Main.ambientVolume, delegate(float v)
        {
            Main.ambientVolume = v;
        }, 4);
        Header(5, () => Language.GetTextValue("GameUI.ZoomCategory"));
        Slider(6, () => Wrapped(CachedText.Format("GameUI.GameZoom", CachedText.Number(Math.Round(Main.GameZoomTarget * 100f)), CachedText.Number(Math.Round(Main.GameViewMatrix.Zoom.X * 100f)))), () => Main.GameZoomTarget - 1f, delegate(float v)
        {
            Main.GameZoomTarget = v + 1f;
        }, 10, 0.85f);
        _uiScaleSlider = Slider(7, () => Wrapped(CachedText.Format("GameUI.UIScale", CachedText.Number(Math.Round(Main.temporaryGUIScaleSlider * 100f)), CachedText.Number(Math.Round(Main.UIScale * 100f)))), () => MathHelper.Clamp((Main.temporaryGUIScaleSlider - 0.5f) / 1.5f, 0f, 1f), delegate(float v)
        {
            Main.temporaryGUIScaleSlider = (float)(int)((v * 1.5f + 0.5f) * 100f) / 100f;
            Main.temporaryGUIScaleSliderUpdate = true;
        }, 11, 0.75f);
        Header(9, () => Language.GetTextValue("GameUI.Gameplay"));
        Toggle(10, () => Lang.menu[Main.autoSave ? 67 : 68].Value, delegate
        {
            Main.autoSave = !Main.autoSave;
        });
        Toggle(11, () => Lang.menu[Main.autoPause ? 69 : 70].Value, delegate
        {
            Main.autoPause = !Main.autoPause;
        });
        Toggle(12, () => Language.GetTextValue(Main.SettingPlayWhenUnfocused ? "UI.PlayWhenUnfocusedOn" : "UI.PlayWhenUnfocusedOff"), delegate
        {
            Main.SettingPlayWhenUnfocused = !Main.SettingPlayWhenUnfocused;
        });
        Toggle(13, () => Lang.menu[Main.ReversedUpDownArmorSetBonuses ? 220 : 221].Value, delegate
        {
            Main.ReversedUpDownArmorSetBonuses = !Main.ReversedUpDownArmorSetBonuses;
        });
        Toggle(14, () => DoorOpeningHelper.PreferenceSettings switch
        {
            DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForEverything => Language.GetTextValue("UI.SmartDoorsEnabled"),
            DoorOpeningHelper.DoorAutoOpeningPreference.EnabledForGamepadOnly => Language.GetTextValue("UI.SmartDoorsGamepad"),
            _ => Language.GetTextValue("UI.SmartDoorsDisabled")
        }, DoorOpeningHelper.CyclePreferences);
        Toggle(15, () => Language.GetTextValue((Player.Settings.HoverControl == ButtonControlMode.Hold) ? "UI.HoverControlSettingIsHold" : "UI.HoverControlSettingIsClick"), Player.Settings.CycleHoverControl);
        Toggle(16, () => Language.GetTextValue(Main.SettingsEnabled_AutoReuseAllItems ? "UI.AutoReuseAllOn" : "UI.AutoReuseAllOff"), delegate
        {
            Main.SettingsEnabled_AutoReuseAllItems = !Main.SettingsEnabled_AutoReuseAllItems;
        });
    }

    private void BuildInterface()
    {
        BeginRight(14);
        Toggle(0, () => Lang.menu[Main.showItemText ? 71 : 72].Value, delegate
        {
            Main.showItemText = !Main.showItemText;
        });
        Toggle(1, () => CachedText.Concat(Lang.menu[123].Value, " ", Lang.menu[124 + Main.invasionProgressMode].Value), delegate
        {
            Main.invasionProgressMode = (Main.invasionProgressMode + 1) % 3;
        });
        Toggle(2, () => Lang.menu[Main.placementPreview ? 128 : 129].Value, delegate
        {
            Main.placementPreview = !Main.placementPreview;
        });
        Toggle(3, () => Lang.inter[ItemSlot.Options.HighlightNewItems ? 117 : 116].Value, delegate
        {
            ItemSlot.Options.HighlightNewItems = !ItemSlot.Options.HighlightNewItems;
        });
        Toggle(4, () => Lang.menu[Main.MouseShowBuildingGrid ? 229 : 230].Value, delegate
        {
            Main.MouseShowBuildingGrid = !Main.MouseShowBuildingGrid;
        });
        Toggle(5, () => Lang.menu[Main.GamepadDisableInstructionsDisplay ? 241 : 242].Value, delegate
        {
            Main.GamepadDisableInstructionsDisplay = !Main.GamepadDisableInstructionsDisplay;
        });
        Toggle(6, () => CachedText.Format("UI.SelectMapBorder", Language.GetTextValue(CachedText.Concat("UI.MinimapFrame_", Main.MinimapFrameManagerInstance.ActiveSelectionKeyName))), Main.MinimapFrameManagerInstance.CycleSelection);
        Slider(7, () => Wrapped(CachedText.Format("GameUI.MapScale", CachedText.Number(Math.Round(Main.MapScale * 100f)))), () => (Main.MapScale - 0.5f) / 0.5f, delegate(float v)
        {
            Main.MapScale = v * 0.5f + 0.5f;
        }, 12, 0.85f);
        Toggle(8, () => CachedText.Format("UI.SelectHealthStyle", Language.GetTextValue(CachedText.Concat("UI.HealthManaStyle_", Main.ResourceSetsManager.ActiveSetKeyName))), Main.ResourceSetsManager.CycleResourceSet);
        Toggle(9, () => Main.DialoguePortraitPreference switch
        {
            Main.DialoguePortraitDrawOption.CloseUp => Language.GetTextValue("UI.PortraitsCloseUp"),
            Main.DialoguePortraitDrawOption.FullBodyRetro => Language.GetTextValue("UI.PortraitsFullBody"),
            Main.DialoguePortraitDrawOption.Disabled => Language.GetTextValue("UI.PortraitsDisabled"),
            _ => Language.GetTextValue("UI.PortraitsDetailed")
        }, Main.CycleNPCPortraitMode);
        Toggle(10, () => Language.GetTextValue(BigProgressBarSystem.ShowText ? "UI.ShowBossLifeTextOn" : "UI.ShowBossLifeTextOff"), BigProgressBarSystem.ToggleShowText);
        Toggle(11, () => Language.GetTextValue(Main.SettingsEnabled_OpaqueBoxBehindTooltips ? "GameUI.HoverTextBoxesOn" : "GameUI.HoverTextBoxesOff"), delegate
        {
            Main.SettingsEnabled_OpaqueBoxBehindTooltips = !Main.SettingsEnabled_OpaqueBoxBehindTooltips;
        });
        Toggle(12, () => ItemSlot.Options.DisableQuickTrash ? Lang.menu[253].Value : Lang.menu[ItemSlot.Options.DisableLeftShiftTrashCan ? 224 : 223].Value, CycleQuickTrash);
        Toggle(13, () => Language.GetTextValue(Main.FlashyEffectsInterface ? "UI.FlashyEffectsInterfaceOn" : "UI.FlashyEffectsInterfaceOff"), delegate
        {
            Main.FlashyEffectsInterface = !Main.FlashyEffectsInterface;
        });
    }

    private static void CycleQuickTrash()
    {
        if (ItemSlot.Options.DisableQuickTrash)
        {
            ItemSlot.Options.DisableQuickTrash = false;
            ItemSlot.Options.DisableLeftShiftTrashCan = true;
        }
        else if (ItemSlot.Options.DisableLeftShiftTrashCan)
        {
            ItemSlot.Options.DisableLeftShiftTrashCan = false;
        }
        else
        {
            ItemSlot.Options.DisableQuickTrash = true;
            ItemSlot.Options.DisableLeftShiftTrashCan = false;
        }
    }

    private static string OnOff(string key, bool enabled)
    {
        return CachedText.Format(key, Language.GetTextValue(enabled ? "GameUI.Enabled" : "GameUI.Disabled"));
    }

    private void BuildVideo()
    {
        // Spaced as in the legacy window, which also lists Frame Skip.
        BeginRight(14, -2f);
        // Resizing the window mid-draw isn't safe, so these wait for the next update.
        Toggle(0, () => Lang.menu[Main.graphics.IsFullScreen ? 49 : 50].Value, delegate
        {
            Main.QueueMainThreadAction(Main.ToggleFullScreen);
        });
        Toggle(1, () => CachedText.Concat(Lang.menu[51].Value, ": ", CachedText.Number(Main.PendingResolutionWidth), "x", CachedText.Number(Main.PendingResolutionHeight)), NextResolution);
        Slider(2, () => CachedText.Concat(Lang.menu[52].Value, ": ", CachedText.Number(Main.bgScroll), "%"), () => Main.bgScroll / 100f, delegate(float v)
        {
            Main.bgScroll = (int)(v * 100f);
            Main.caveParallax = 1f - Main.bgScroll / 500f;
        }, 1);
        Toggle(3, () => Language.GetTextValue(CachedText.Concat("UI.LightMode_", CachedText.Name(Lighting.Mode))), Lighting.NextLightMode);
        Toggle(4, () => Lang.menu[59 + Main.qaStyle].Value, delegate
        {
            if (++Main.qaStyle > 3)
            {
                Main.qaStyle = 0;
            }
        });
        Toggle(5, () => Lang.menu[Main.BackgroundEnabled ? 100 : 101].Value, delegate
        {
            Main.BackgroundEnabled = !Main.BackgroundEnabled;
        });
        Toggle(6, () => Lang.menu[ChildSafety.Disabled ? 132 : 133].Value, delegate
        {
            ChildSafety.Disabled = !ChildSafety.Disabled;
        });
        Toggle(7, () => OnOff("GameUI.ForegroundSunlightEffects", Main.ForegroundSunlightEffects), delegate
        {
            Main.ForegroundSunlightEffects = !Main.ForegroundSunlightEffects;
        });
        Toggle(8, () => OnOff("GameUI.HeatDistortion", Main.UseHeatDistortion), delegate
        {
            Main.UseHeatDistortion = !Main.UseHeatDistortion;
        });
        Toggle(9, () => OnOff("GameUI.StormEffects", Main.UseStormEffects), delegate
        {
            Main.UseStormEffects = !Main.UseStormEffects;
        });
        Toggle(10, () => CachedText.Format("GameUI.WaveQuality", Main.WaveQuality switch
        {
            1 => Language.GetTextValue("GameUI.QualityLow"),
            2 => Language.GetTextValue("GameUI.QualityMedium"),
            3 => Language.GetTextValue("GameUI.QualityHigh"),
            _ => Language.GetTextValue("GameUI.QualityOff")
        }), delegate
        {
            Main.WaveQuality = (Main.WaveQuality + 1) % 4;
        });
        Toggle(11, () => Language.GetTextValue(Main.SettingsEnabled_TilesSwayInWind ? "UI.TilesSwayInWindOn" : "UI.TilesSwayInWindOff"), delegate
        {
            Main.SettingsEnabled_TilesSwayInWind = !Main.SettingsEnabled_TilesSwayInWind;
        });
        Toggle(12, () => OnOff("GameUI.ScreenShake", Main.UseScreenShake), delegate
        {
            Main.UseScreenShake = !Main.UseScreenShake;
        });
        Toggle(13, () => Language.GetTextValue(Main.FlashyEffectsWorld ? "UI.FlashyEffectsWorldOn" : "UI.FlashyEffectsWorldOff"), delegate
        {
            Main.FlashyEffectsWorld = !Main.FlashyEffectsWorld;
        });
    }

    private static void NextResolution()
    {
        int current = 0;
        for (int i = 0; i < Main.numDisplayModes; i++)
        {
            if (Main.displayWidth[i] == Main.PendingResolutionWidth && Main.displayHeight[i] == Main.PendingResolutionHeight)
            {
                current = i;
                break;
            }
        }
        int next = (current + 1) % Main.numDisplayModes;
        Main.PendingResolutionWidth = Main.displayWidth[next];
        Main.PendingResolutionHeight = Main.displayHeight[next];
        Main.QueueMainThreadAction(delegate
        {
            Main.SetResolution(Main.PendingResolutionWidth, Main.PendingResolutionHeight);
        });
    }

    private void BuildCursor()
    {
        BeginRight(15);
        Header(0, () => Lang.menu[64].Value);
        ColorSlider(1, 5, 25, DelegateMethods.ColorLerp_HSL_H, () => Main.mouseColorSlider.Hue, delegate(float v)
        {
            Main.mouseColorSlider.Hue = v;
        }, () => Main.mouseColorSlider.GetHSLVector());
        ColorSlider(2, 6, 25, DelegateMethods.ColorLerp_HSL_S, () => Main.mouseColorSlider.Saturation, delegate(float v)
        {
            Main.mouseColorSlider.Saturation = v;
        }, () => Main.mouseColorSlider.GetHSLVector());
        // The cursor's own luminance is kept at 0.15 or more, so it stays visible.
        ColorSlider(3, 7, 25, DelegateMethods.ColorLerp_HSL_L, () => Utils.GetLerpValue(0.15f, 1f, Main.mouseColorSlider.Luminance, clamped: true), delegate(float v)
        {
            Main.mouseColorSlider.Luminance = v * 0.85f + 0.15f;
        }, delegate
        {
            Vector3 hsl = Main.mouseColorSlider.GetHSLVector();
            hsl.Z = Utils.GetLerpValue(0.15f, 1f, hsl.Z, clamped: true);
            return hsl;
        });
        Header(5, () => Lang.menu[217].Value);
        ColorSlider(6, 5, 252, DelegateMethods.ColorLerp_HSL_H, () => Main.mouseBorderColorSlider.Hue, delegate(float v)
        {
            Main.mouseBorderColorSlider.Hue = v;
        }, () => Main.mouseBorderColorSlider.GetHSLVector());
        ColorSlider(7, 6, 252, DelegateMethods.ColorLerp_HSL_S, () => Main.mouseBorderColorSlider.Saturation, delegate(float v)
        {
            Main.mouseBorderColorSlider.Saturation = v;
        }, () => Main.mouseBorderColorSlider.GetHSLVector());
        ColorSlider(8, 7, 252, DelegateMethods.ColorLerp_HSL_L, () => Main.mouseBorderColorSlider.Luminance, delegate(float v)
        {
            Main.mouseBorderColorSlider.Luminance = v;
        }, () => Main.mouseBorderColorSlider.GetHSLVector());
        ColorSlider(9, 8, 252, DelegateMethods.ColorLerp_HSL_O, () => Main.mouseBorderColorSlider.Alpha, delegate(float v)
        {
            Main.mouseBorderColorSlider.Alpha = v;
        }, () => Main.mouseBorderColorSlider.GetHSLVector());
        Toggle(11, () => LockOnHelper.UseMode switch
        {
            LockOnHelper.LockOnMode.FocusTarget => Lang.menu[232].Value,
            LockOnHelper.LockOnMode.TargetClosest => Lang.menu[233].Value,
            LockOnHelper.LockOnMode.ThreeDS => Lang.menu[234].Value,
            _ => ""
        }, LockOnHelper.CycleUseModes, 0.9f);
        Toggle(12, () => Lang.menu[Player.SmartCursorSettings.SmartBlocksEnabled ? 215 : 216].Value, delegate
        {
            Player.SmartCursorSettings.SmartBlocksEnabled = !Player.SmartCursorSettings.SmartBlocksEnabled;
        }, 0.9f);
        Toggle(13, () => Lang.menu[Main.cSmartCursorModeIsToggleAndNotHold ? 121 : 122].Value, delegate
        {
            Main.cSmartCursorModeIsToggleAndNotHold = !Main.cSmartCursorModeIsToggleAndNotHold;
        });
        Toggle(14, () => Lang.menu[Player.SmartCursorSettings.SmartAxeAfterPickaxe ? 214 : 213].Value, delegate
        {
            Player.SmartCursorSettings.SmartAxeAfterPickaxe = !Player.SmartCursorSettings.SmartAxeAfterPickaxe;
        }, 0.9f);
    }

    /// <summary>
    /// One of the cursor colour bars. While focused it sets menuMode to 25 (cursor) or 252 (border):
    /// that's how HandleOptionsSpecials knows which colour the gamepad is adjusting.
    /// </summary>
    private void ColorSlider(int slot, int gamepadFeature, int menuMode, Utils.ColorLerpMethod ramp, Func<float> getValue, Action<float> setValue, Func<Vector3> rampColor)
    {
        UIMenuSlider slider = Slider(slot, () => "", getValue, setValue, gamepadFeature, 1f, _cursorBarShift, ramp, menuMode);
        slider.BeforeDraw = delegate
        {
            DelegateMethods.v3_1 = rampColor();
        };
        slider.WhileFocused = delegate
        {
            Main.menuMode = menuMode;
        };
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Vector2 size = new Vector2(PanelWidth, PanelHeight);
        Utils.DrawInvBG(spriteBatch, _origin.X - Margin, _origin.Y - Margin, PanelWidth + Margin * 2f, PanelHeight + Margin * 2f, new Color(33, 15, 91, 255) * 0.685f);
        Utils.DrawBorderString(spriteBatch, Language.GetTextValue("GameUI.SettingsMenu"), _origin + size * new Vector2(0.5f, 0f), Color.White, 1f, 0.5f);
        float split = _narrow ? (PanelWidth / 3f) : (PanelWidth / 2f);
        Utils.DrawInvBG(spriteBatch, _origin.X + Margin / 2f, _origin.Y + Margin * 5f / 2f, split - Margin, PanelHeight - Margin * 3f);
        Utils.DrawInvBG(spriteBatch, _origin.X + split + Margin, _origin.Y + Margin * 5f / 2f, PanelWidth - split - Margin * 3f / 2f, PanelHeight - Margin * 3f);
    }
}
