using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ConfigurationManager;
using HarmonyLib;
using UnityEngine;
using StockCM = ConfigurationManager.ConfigurationManager;

namespace BetterBepInEx
{
    /// <summary>
    /// Replaces the window of the F12 menu (BepInEx Configuration Manager, as SPT ships it) with
    /// a larger one: a sidebar of mods, search across every setting, changed-from-default markers,
    /// undo, and a keybind page that shows collisions.
    ///
    /// The stock plugin keeps running underneath. Its hotkey still opens the menu, its cursor
    /// handling still applies, and its setting list (with every mod's attributes already read) is
    /// what this window shows. Only its OnGUI is skipped. Turning "Use the new menu" off brings the
    /// stock window back at once.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(StockCM.GUID)]
    public class BetterBepInExPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.betterbepinex";
        public const string PluginName = "BetterBepInEx";

        /// <summary>Must match the csproj's Version. Two places, and they have to agree.</summary>
        public const string PluginVersion = "0.4.1";

        private const string GameFontHint = "Bender";

        internal static ManualLogSource Log;
        internal static BetterBepInExPlugin Instance;

        internal ConfigEntry<bool> UseNewMenu;
        internal ConfigEntry<bool> HoldPlayer;
        internal ConfigEntry<bool> DimBackground;
        internal ConfigEntry<bool> ClickOffToPlay;
        internal ConfigEntry<float> BackgroundOpacity;
        internal ConfigEntry<bool> ShowDescriptions;
        internal ConfigEntry<bool> ShowHidden;
        internal ConfigEntry<float> MenuScale;
        internal ConfigEntry<bool> ScaleWithScreen;
        internal ConfigEntry<bool> UseGameFont;
        internal ConfigEntry<bool> ResetWindow;
        internal ConfigEntry<string> WindowRect;
        internal ConfigEntry<string> Favorites;
        internal ConfigEntry<string> LastPage;
        internal ConfigEntry<MenuTheme> Theme;
        internal ConfigEntry<Color> AccentColor;
        internal ConfigEntry<float> TextSize;
        internal ConfigEntry<bool> CompactRows;
        internal ConfigEntry<ModSort> SortMods;
        internal ConfigEntry<string> RenamedMods;
        internal ConfigEntry<string> HiddenMods;
        internal ConfigEntry<string> RecentMods;

        private StockManager _stock;
        private SettingsModel _model;
        private MenuWindow _menu;
        private readonly InputBlocker _blocker = new InputBlocker();
        private Harmony _harmony;
        private bool _ready;
        private bool _open;

        /// <summary>The <see cref="StockManager.Signature"/> the model was last built at; -1 before the first build.</summary>
        private long _builtSignature = -1;
        private Font _gameFont;
        private bool _gameFontSearched;

        /// <summary>True when this menu is drawing in place of the stock window.</summary>
        internal static bool Active => Instance != null && Instance._ready && Instance.UseNewMenu.Value;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            UseNewMenu = Config.Bind("General", "Use the new menu", true,
                "Show this menu when F12 is pressed. Off brings back the stock menu straight away.");
            HoldPlayer = Config.Bind("General", "Hold the player still while open", true,
                "In raid, stop the player moving and the camera turning while the menu is open, "
                + "the way the inventory does. The stock menu lets both through.");
            ClickOffToPlay = Config.Bind("General", "Click outside to keep playing", true,
                "Clicking outside the menu fades it and gives the game your mouse and keys back, so you "
                + "can move around with it open. Click the menu to use it again; F12 still closes it. "
                + "Off keeps the menu in front until you close it.");
            BackgroundOpacity = Config.Bind("Look", "Opacity in the background", 0.45f, new ConfigDescription(
                "How see-through the menu is after clicking outside it. 1 is solid.",
                new AcceptableValueRange<float>(0.1f, 1f)));
            DimBackground = Config.Bind("General", "Dim the game behind the menu", true,
                "Darken the screen behind the menu.");
            ShowDescriptions = Config.Bind("General", "Show descriptions under settings", true,
                "Print each setting's description under its name. Off shows it when you hover the name instead.");
            ShowHidden = Config.Bind("General", "Show hidden and advanced", false,
                "Show settings their mod marks as advanced. Search always finds them.");

            Theme = Config.Bind("Look", "Theme", MenuTheme.Dark,
                "The menu's colours. Black suits OLED screens; High contrast is easiest to read.");
            AccentColor = Config.Bind("Look", "Accent color", new Color(0.839f, 0.718f, 0.431f, 1f),
                "The colour of switches, sliders, badges and the selected mod. EFT's tan by default.");
            TextSize = Config.Bind("Look", "Text size", 1f, new ConfigDescription(
                "Size of the text, on top of Menu scale: bigger text in the same window, or smaller.",
                new AcceptableValueRange<float>(0.8f, 1.5f)));
            CompactRows = Config.Bind("Look", "Compact rows", false,
                "Less space around each setting, so more fit on screen.");
            SortMods = Config.Bind("Mods list", "Sort mods by", ModSort.Name,
                "The order of the mods list. Recently opened puts the mods you opened last at the top, "
                + "the next time the menu opens. Author groups each author's mods under their name.");
            MenuScale = Config.Bind("Look", "Menu scale", 1f, new ConfigDescription(
                "Size of the menu text and controls. 1 is the normal size for your screen.",
                new AcceptableValueRange<float>(0.6f, 2f)));
            ScaleWithScreen = Config.Bind("Look", "Scale with screen resolution", true,
                "Size the menu for your resolution, so it reads the same at 1080p, 1440p, ultrawide and 4K. "
                + "Off draws it at 1080p size on any screen.");
            UseGameFont = Config.Bind("Look", "Use the game font", false,
                "Draw the menu in EFT's own font, if the game has it loaded. If the menu text disappears, "
                + "set this back to false in BepInEx\\config\\" + PluginGuid + ".cfg.");
            ResetWindow = Config.Bind("Look", "Reset window position", false,
                "Put the menu back in the middle of the screen at its default size.");
            ResetWindow.SettingChanged += (_, __) =>
            {
                if (ResetWindow.Value)
                {
                    ResetWindow.Value = false;
                    _menu?.ResetWindow();
                }
            };
            UseGameFont.SettingChanged += (_, __) => _gameFontSearched = false;
            UseNewMenu.SettingChanged += (_, __) =>
            {
                // The stock list is not built while this menu is on, so build it before the stock
                // window draws, or it would come up empty.
                if (!UseNewMenu.Value && _ready)
                {
                    _stock.Instance.BuildSettingList();
                }
            };

            // Kept between sessions; the "Hidden" tag keeps them off the menu's own page.
            WindowRect = Config.Bind("Window", "Rect", string.Empty, new ConfigDescription("Window position and size, as fractions of the screen.", null, "Hidden"));
            Favorites = Config.Bind("Window", "Favorites", string.Empty, new ConfigDescription("GUIDs of favourite mods.", null, "Hidden"));
            LastPage = Config.Bind("Window", "Last page", string.Empty, new ConfigDescription("The page open when the menu closed.", null, "Hidden"));
            RenamedMods = Config.Bind("Window", "Renamed mods", string.Empty, new ConfigDescription("Names given to mods in the list, one GUID and name per line.", null, "Hidden"));
            HiddenMods = Config.Bind("Window", "Hidden mods", string.Empty, new ConfigDescription("GUIDs of mods hidden from the list.", null, "Hidden"));
            RecentMods = Config.Bind("Window", "Recent mods", string.Empty, new ConfigDescription("GUIDs of mods opened most recently, newest first.", null, "Hidden"));

            _stock = new StockManager();
            if (!_stock.Resolve(Logger))
            {
                return;
            }
            _blocker.Resolve(Logger);

            _model = new SettingsModel(_stock);
            _menu = new MenuWindow(this, _stock, _model);

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.Patch(AccessTools.Method(typeof(StockCM), "OnGUI"),
                    prefix: new HarmonyMethod(typeof(BetterBepInExPlugin), nameof(SkipStockWindow)) { priority = Priority.First });
                _harmony.Patch(AccessTools.Method(typeof(StockCM), nameof(StockCM.BuildSettingList)),
                    prefix: new HarmonyMethod(typeof(BetterBepInExPlugin), nameof(InsteadOfStockBuild)));
            }
            catch (Exception e)
            {
                _harmony.UnpatchSelf();
                Logger.LogError("Could not patch the stock Configuration Manager, so the stock menu is left as it is: " + e);
                return;
            }

            _ready = true;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded"
                           + (_blocker.Available ? "." : ", without holding the raid player still."));
        }

        /// <summary>
        /// Every plugin's Awake has run by now (the chainloader loads them all before the first
        /// frame), so their settings are bound. Indexing them here, behind the game's loading
        /// screen, is what makes the first F12 press instant.
        /// </summary>
        private void Start()
        {
            if (!_ready)
            {
                return;
            }
            if (UseNewMenu.Value)
            {
                RemoveOtherWindowPatches();
            }
            RebuildModel();
        }

        /// <summary>
        /// Other mods that restyle the stock window (acidphantasm's scale and resize add-on is the
        /// common one) patch its OnGUI. Their prefixes and postfixes still run when the original is
        /// skipped, and would draw a resize grip and eat clicks where the stock window would have
        /// been. This menu replaces that window, so those patches are taken off. Turning the new
        /// menu off later does not put them back until the game restarts.
        /// </summary>
        private void RemoveOtherWindowPatches()
        {
            foreach (string name in new[] { "OnGUI", "SettingsWindow" })
            {
                MethodInfo method = AccessTools.Method(typeof(StockCM), name);
                Patches info = method == null ? null : Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    continue;
                }
                IEnumerable<Patch> all = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers);
                foreach (Patch patch in all.Where(p => p.owner != PluginGuid).ToList())
                {
                    _harmony.Unpatch(method, patch.PatchMethod);
                    Logger.LogInfo("Took " + patch.PatchMethod.DeclaringType?.FullName + "." + patch.PatchMethod.Name
                                   + " (" + patch.owner + ") off the stock window's " + name
                                   + "; this menu replaces that window. Restart with the new menu off to get it back.");
                }
            }
        }

        private static bool SkipStockWindow() => !Active;

        /// <summary>
        /// The stock plugin rebuilds its whole list on every open: a FindObjectsOfType scan, a
        /// wrapper per setting, and a DLL read per mod. While this menu is on that list is never
        /// shown, so the work is skipped. A mod that calls BuildSettingList because it added
        /// settings still gets them shown: the signature check sees the new count.
        /// </summary>
        private static bool InsteadOfStockBuild()
        {
            if (!Active)
            {
                return true;
            }
            Instance.RebuildIfChanged();
            return false;
        }

        private void RebuildIfChanged()
        {
            if (_stock.Signature() != _builtSignature)
            {
                RebuildModel();
            }
        }

        private void RebuildModel()
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            long signature = _stock.Signature();
            List<BaseUnityPlugin> withoutSettings = new List<BaseUnityPlugin>();
            List<SettingEntryBase> settings = _stock.CollectSettings(Logger, withoutSettings);
            // The stock menu hides its own settings, F12 included. Show its hotkey on this page,
            // where someone looking for how to change F12 will look.
            List<SettingEntryBase> extra = new List<SettingEntryBase>();
            if (_stock.Hotkey != null)
            {
                extra.Add(_stock.Wrap(_stock.Hotkey, this));
            }
            _model.Build(settings, extra, withoutSettings);
            _builtSignature = signature;
            _menu.OnModelRebuilt();
            Logger.LogInfo("Indexed " + _model.AllRows.Count + " settings from " + _model.Pages.Count
                           + " mods in " + watch.ElapsedMilliseconds + " ms.");
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            bool open = UseNewMenu.Value && _stock.DisplayingWindow;
            if (open && !_open)
            {
                // Normally already done by the BuildSettingList prefix when the stock plugin
                // opened; this covers switching over from the stock window while it is open.
                RebuildIfChanged();
                _menu.OnOpen();
            }
            else if (!open && _open)
            {
                _menu.OnClose();
                _blocker.Release();
            }
            _open = open;

            if (!open)
            {
                return;
            }
            if (HoldPlayer.Value && !_menu.Passive)
            {
                _blocker.Hold();
            }
            else
            {
                _blocker.Release();
            }
            _menu.Update();
        }

        private void OnGUI()
        {
            if (_ready && _open)
            {
                _menu.OnGUI();
            }
        }

        internal float EffectiveScale()
        {
            return Core.ScreenScale.Effective(Screen.width, Screen.height, MenuScale.Value, ScaleWithScreen.Value);
        }

        /// <summary>
        /// EFT's UI font, if "Use the game font" is on and the game has a dynamic Font asset by that
        /// name loaded. Most of EFT's text is TextMeshPro, which IMGUI cannot use, so this may find
        /// nothing; the default font is used then.
        /// </summary>
        internal Font GameFont()
        {
            if (!UseGameFont.Value)
            {
                return null;
            }
            if (!_gameFontSearched)
            {
                _gameFontSearched = true;
                _gameFont = Resources.FindObjectsOfTypeAll<Font>()
                    .FirstOrDefault(f => f != null && f.dynamic && f.name.IndexOf(GameFontHint, StringComparison.OrdinalIgnoreCase) >= 0);
                Logger.LogInfo(_gameFont == null
                    ? "No dynamic font named like '" + GameFontHint + "' is loaded; using the default font."
                    : "Using the game font '" + _gameFont.name + "'.");
            }
            return _gameFont;
        }

        private void OnDestroy()
        {
            _blocker.Release();
        }
    }
}
