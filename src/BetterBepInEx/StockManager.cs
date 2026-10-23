using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ConfigurationManager;
using HarmonyLib;
using UnityEngine;
using StockCM = ConfigurationManager.ConfigurationManager;

namespace BetterBepInEx
{
    /// <summary>
    /// Everything this mod needs from the stock Configuration Manager that SPT ships (BepInEx
    /// ConfigurationManager 18.4). The stock plugin stays loaded and keeps doing three jobs: its
    /// hotkey opens and closes the menu, it unlocks the cursor while the menu is open, and it
    /// builds the setting list with every mod's ConfigurationManagerAttributes already read. Only
    /// its window is skipped, by the prefix in <see cref="Patches"/>.
    ///
    /// Its public surface is used directly. Its internals (the setting list, the field drawer, the
    /// column widths the drawer reads) are reached here by name, and <see cref="Resolve"/> refuses
    /// to run if any of them is missing, so a different build of the stock plugin turns this mod
    /// off with a log line instead of throwing in the middle of OnGUI.
    /// </summary>
    internal sealed class StockManager
    {
        public StockCM Instance { get; private set; }

        /// <summary>The stock menu's own hotkey, F12 by default. Shown on this mod's page.</summary>
        public ConfigEntry<KeyboardShortcut> Hotkey { get; private set; }

        private object _fieldDrawer;
        private Action<SettingEntryBase> _drawSettingValue;
        private Func<bool> _drawCurrentDropdown;
        private Func<bool> _stockCapturingKey;
        private MethodInfo _setLeftColumn;
        private MethodInfo _setRightColumn;
        private MethodInfo _setWindowRect;
        private PropertyInfo _entryProperty;
        private ConstructorInfo _configSettingEntryCtor;
        private Dictionary<Type, Action<SettingEntryBase>> _drawHandlers;
        private MethodInfo _setPluginInfo;
        private MethodInfo _setIsAdvanced;
        private ConfigFile _coreConfig;
        private BepInPlugin _coreMeta;

        public bool Resolve(ManualLogSource log)
        {
            if (!Chainloader.PluginInfos.TryGetValue(StockCM.GUID, out PluginInfo info) || info.Instance == null)
            {
                log.LogError("The stock Configuration Manager (" + StockCM.GUID + ") is not loaded.");
                return false;
            }
            Instance = (StockCM)info.Instance;

            try
            {
                Type cm = typeof(StockCM);
                Type drawer = AccessTools.TypeByName("ConfigurationManager.SettingFieldDrawer");
                Type configEntry = AccessTools.TypeByName("ConfigurationManager.ConfigSettingEntry");

                Hotkey = (ConfigEntry<KeyboardShortcut>)Need(AccessTools.Field(cm, "_keybind"), "_keybind").GetValue(Instance);

                _fieldDrawer = Need(AccessTools.Field(cm, "_fieldDrawer"), "_fieldDrawer").GetValue(null);
                MethodInfo drawValue = Need(AccessTools.Method(drawer, "DrawSettingValue"), "DrawSettingValue");
                _drawSettingValue = (Action<SettingEntryBase>)Delegate.CreateDelegate(
                    typeof(Action<SettingEntryBase>), _fieldDrawer, drawValue);
                _drawCurrentDropdown = (Func<bool>)Delegate.CreateDelegate(
                    typeof(Func<bool>), Need(AccessTools.Method(drawer, "DrawCurrentDropdown"), "DrawCurrentDropdown"));
                _stockCapturingKey = (Func<bool>)Delegate.CreateDelegate(
                    typeof(Func<bool>), Need(AccessTools.PropertyGetter(drawer, "SettingKeyboardShortcut"), "SettingKeyboardShortcut"));
                _drawHandlers = (Dictionary<Type, Action<SettingEntryBase>>)
                    Need(AccessTools.PropertyGetter(drawer, "SettingDrawHandlers"), "SettingDrawHandlers").Invoke(null, null);

                _setLeftColumn = Need(AccessTools.PropertySetter(cm, "LeftColumnWidth"), "LeftColumnWidth");
                _setRightColumn = Need(AccessTools.PropertySetter(cm, "RightColumnWidth"), "RightColumnWidth");
                _setWindowRect = Need(AccessTools.PropertySetter(cm, "SettingWindowRect"), "SettingWindowRect");

                _entryProperty = Need(AccessTools.Property(configEntry, "Entry"), "ConfigSettingEntry.Entry");
                _configSettingEntryCtor = Need(
                    AccessTools.Constructor(configEntry, new[] { typeof(ConfigEntryBase), typeof(BaseUnityPlugin) }),
                    "ConfigSettingEntry(ConfigEntryBase, BaseUnityPlugin)");

                _setPluginInfo = Need(AccessTools.PropertySetter(typeof(SettingEntryBase), "PluginInfo"), "SettingEntryBase.PluginInfo");
                _setIsAdvanced = Need(AccessTools.PropertySetter(typeof(SettingEntryBase), "IsAdvanced"), "SettingEntryBase.IsAdvanced");
                _coreConfig = (ConfigFile)Need(AccessTools.Property(typeof(ConfigFile), "CoreConfig"), "ConfigFile.CoreConfig").GetValue(null, null);
                _coreMeta = new BepInPlugin("BepInEx", "BepInEx", typeof(Chainloader).Assembly.GetName().Version.ToString());
            }
            catch (Exception e)
            {
                log.LogError("The stock Configuration Manager is not the build this mod knows (" + e.Message
                             + "). The stock F12 menu is left as it is.");
                return false;
            }
            return true;
        }

        private static T Need<T>(T member, string name) where T : class
        {
            if (member == null)
            {
                throw new MissingMemberException(name);
            }
            return member;
        }

        public bool DisplayingWindow
        {
            get => Instance.DisplayingWindow;
            set => Instance.DisplayingWindow = value;
        }

        /// <summary>
        /// Stops the stock hotkey check. Set while this menu is capturing a key, so pressing F12
        /// to bind it does not also close the menu.
        /// </summary>
        public bool OverrideHotkey
        {
            get => Instance.OverrideHotkey;
            set => Instance.OverrideHotkey = value;
        }

        public ConfigEntryBase EntryOf(SettingEntryBase setting)
        {
            return setting != null && _entryProperty.DeclaringType.IsInstanceOfType(setting)
                ? (ConfigEntryBase)_entryProperty.GetValue(setting, null)
                : null;
        }

        /// <summary>
        /// Collects every setting the way the stock SettingSearcher does (BepInEx's core config as
        /// advanced settings, then each loaded plugin's config), but without its two slow parts: a
        /// FindObjectsOfType scan of the scene for plugins the chainloader did not load, and the
        /// per-plugin DLL version-info read for the website, which the stock menu repeats on
        /// every open. Plugins outside the chainloader are therefore not listed.
        ///
        /// Unlike the stock search it keeps what authors hide ([Browsable(false)] on the plugin,
        /// Browsable = false on an entry); the model marks those rows hidden and the menu shows
        /// them only with "Show hidden". Plugins with no settings at all go in
        /// <paramref name="withoutSettings"/>, so that view can list every loaded mod. The stock
        /// Configuration Manager itself is left out: its F12 key is already on this mod's page,
        /// and its other settings do nothing while this window replaces its own.
        /// </summary>
        public List<SettingEntryBase> CollectSettings(ManualLogSource log, List<BaseUnityPlugin> withoutSettings)
        {
            List<SettingEntryBase> results = new List<SettingEntryBase>();
            if (_coreConfig != null)
            {
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in _coreConfig)
                {
                    SettingEntryBase s = Wrap(kv.Value, null);
                    _setPluginInfo.Invoke(s, new object[] { _coreMeta });
                    _setIsAdvanced.Invoke(s, new object[] { (bool?)true });
                    results.Add(s);
                }
            }

            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                BaseUnityPlugin plugin = info.Instance;
                if (plugin == null || plugin == Instance)
                {
                    continue;
                }
                try
                {
                    if (plugin.Config.Count == 0)
                    {
                        withoutSettings.Add(plugin);
                        continue;
                    }
                    foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in plugin.Config)
                    {
                        results.Add(Wrap(kv.Value, plugin));
                    }
                }
                catch (Exception e)
                {
                    log.LogWarning("Could not read the settings of " + info.Metadata?.Name + ": " + e.Message);
                }
            }
            return results;
        }

        internal static bool IsHidden(Type type)
        {
            foreach (object attribute in type.GetCustomAttributes(typeof(System.ComponentModel.BrowsableAttribute), false))
            {
                if (!((System.ComponentModel.BrowsableAttribute)attribute).Browsable)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A cheap fingerprint of what <see cref="CollectSettings"/> would return: the number of
        /// plugins and of config entries. Mods that bind settings late (after a raid starts, say)
        /// change it, and the menu rebuilds; otherwise the list built at startup is reused.
        /// </summary>
        public long Signature()
        {
            long signature = _coreConfig == null ? 0 : _coreConfig.Count;
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                BaseUnityPlugin plugin = info.Instance;
                if (plugin != null)
                {
                    signature = signature * 31 + plugin.Config.Count + 1;
                }
            }
            return signature;
        }

        /// <summary>
        /// Wraps a config entry the stock list leaves out (its own hotkey: the stock plugin hides
        /// itself) so this menu can show it, filed under <paramref name="owner"/>'s page.
        /// </summary>
        public SettingEntryBase Wrap(ConfigEntryBase entry, BaseUnityPlugin owner)
        {
            return (SettingEntryBase)_configSettingEntryCtor.Invoke(new object[] { entry, owner });
        }

        /// <summary>
        /// True when the stock drawer has a handler registered for this type that this menu does
        /// not draw itself: vectors, quaternions, and any type a mod registered through
        /// <c>RegisterCustomSettingDrawer</c>.
        /// </summary>
        public bool HasStockHandler(Type type) => _drawHandlers.ContainsKey(type);

        /// <summary>True while a mod's CustomHotkeyDrawer, drawn by the stock drawer, is listening for a key.</summary>
        public bool StockCapturingKey => _stockCapturingKey();

        /// <summary>
        /// Draws one value with the stock drawer, inside this menu's layout. The stock drawer sizes
        /// some fields from the stock window's column widths and places dropdowns against its
        /// window rect, so those are pointed at this menu's geometry first.
        /// </summary>
        public void DrawValue(SettingEntryBase setting)
        {
            _drawSettingValue(setting);
        }

        public void SetGeometry(Rect window, int nameWidth, int valueWidth)
        {
            _setWindowRect.Invoke(Instance, new object[] { window });
            _setLeftColumn.Invoke(Instance, new object[] { nameWidth });
            _setRightColumn.Invoke(Instance, new object[] { valueWidth });
        }

        /// <summary>Draws the stock drawer's open dropdown, if a delegated field opened one.</summary>
        public bool DrawStockDropdown() => _drawCurrentDropdown();
    }
}
