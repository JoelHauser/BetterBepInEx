using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using ConfigurationManager;
using BetterBepInEx.Core;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>How a row's value is drawn. Decided once, when the list is built.</summary>
    internal enum RowKind
    {
        Bool,
        Range,
        Number,
        Text,
        Converted,
        Enum,
        Flags,
        List,
        Shortcut,
        KeyCode,
        Color,

        /// <summary>
        /// Drawn by the stock drawer: CustomDrawer and CustomHotkeyDrawer callbacks, vectors,
        /// quaternions, types a mod registered a drawer for, and anything unknown.
        /// </summary>
        Stock,
    }

    /// <summary>The filter chip a row falls under.</summary>
    internal enum RowCategory
    {
        Toggle,
        Number,
        List,
        Keybind,
        Text,
        Other,
    }

    internal sealed class Row
    {
        public SettingEntryBase Setting;

        /// <summary>The config entry behind the setting; null for the stock debug-only property rows.</summary>
        public ConfigEntryBase Entry;

        public Page Page;
        public string Section;
        public RowKind Kind;
        public RowCategory Category;

        /// <summary>The name with the description as its tooltip, for when descriptions are not printed.</summary>
        public GUIContent Name;

        /// <summary>The name alone, for when the description is printed under it.</summary>
        public GUIContent NamePlain;

        /// <summary>The Reset link with "Back to (default)" as its tooltip, made on first draw.</summary>
        public GUIContent ResetLabel;

        /// <summary>The keycaps last drawn for a keybind row, and the value they were made from.</summary>
        public GUIContent[] KeyCaps;
        public object KeyCapsValue;

        public string Description;
        public GUIContent DescriptionContent;

        /// <summary>IMGUI control name for this row's text field, so focus can be checked by row.</summary>
        public string ControlName;
        public string Haystack;

        /// <summary>The haystack as built, before a player's name for the mod is added to it.</summary>
        public string BaseHaystack;
        public bool IsChanged;
        public bool IsAdvanced;

        /// <summary>
        /// Hidden by its author: Browsable = false on the entry, or [Browsable(false)] on the
        /// plugin. Shown only with "Show hidden", tagged, and left out of counts and conflicts
        /// otherwise.
        /// </summary>
        public bool IsHidden;
        public bool IsReadOnly;
        public bool CanReset;

        /// <summary>
        /// The key that opens this menu. BepInEx's default for it is F1, but SPT ships it as F12, so
        /// it never counts as changed and is never reset or unbound: either would leave the player
        /// with a menu that no longer opens on the key they know.
        /// </summary>
        public bool IsMenuHotkey;
        public object[] Options;
        public GUIContent[] OptionLabels;

        /// <summary>The value as of the last change this model saw, so the next change knows what it replaced.</summary>
        public object LastValue;

        /// <summary>Clipboard id: GUID, section and key. Null when there is no config entry.</summary>
        public string Id;

        /// <summary>
        /// The value's text as the row's field shows it, and the value it was made from. Formatting
        /// a number allocates a string, and the field is drawn several times a frame.
        /// </summary>
        public string ValueText;
        public object ValueTextOf;

        private string _defaultSerialized;

        public void InitDefault()
        {
            if (Entry != null)
            {
                try
                {
                    _defaultSerialized = TomlTypeConverter.ConvertToString(Entry.DefaultValue, Entry.SettingType);
                }
                catch (Exception)
                {
                    _defaultSerialized = null;
                }
            }
            LastValue = Setting.Get();
            RefreshChanged();
        }

        public void RefreshChanged()
        {
            if (IsMenuHotkey)
            {
                IsChanged = false;
                return;
            }
            object def = Setting.DefaultValue;
            if (def == null && !Setting.SettingType.IsClass)
            {
                IsChanged = false;
                return;
            }
            if (Entry != null && _defaultSerialized != null)
            {
                IsChanged = Entry.GetSerializedValue() != _defaultSerialized;
            }
            else
            {
                IsChanged = !Equals(Setting.Get(), def);
            }
        }
    }

    internal sealed class Section
    {
        public string Name;
        public GUIContent Label;
        public readonly List<Row> Rows = new List<Row>();
    }

    internal sealed class Page
    {
        public string Guid;
        public string DisplayName;

        /// <summary>The readable name worked out from the plugin's own; what a rename falls back to.</summary>
        public string AutoName;

        /// <summary>The name the player gave this mod, or null.</summary>
        public string CustomName;

        /// <summary>Hidden from the mods list by the player (shown again by "Show hidden mods").</summary>
        public bool UserHidden;

        public string BaseSubtitle;
        public string OriginalName;
        public string SortKey;
        public string Version;
        public string Author;
        private string _authorUpper;

        /// <summary>The author as the mods list heads a group with it, made once.</summary>
        public string AuthorUpper => _authorUpper ?? (_authorUpper = Author?.ToUpperInvariant());
        public string Website;
        public BaseUnityPlugin Plugin;
        public readonly List<Section> Sections = new List<Section>();
        public readonly List<Row> Rows = new List<Row>();
        public int ChangedCount;
        public int VisibleCount;

        /// <summary>A loaded plugin with no config entries at all; listed only with "Show hidden".</summary>
        public bool HasNoSettings;

        /// <summary>Every row is hidden, or there are none: the page is in the sidebar only with "Show hidden".</summary>
        public bool IsHidden;
        public GUIContent SidebarLabel;

        /// <summary>Lower-cased names for the sidebar's filter box, made on first use.</summary>
        public string SidebarHaystack;
        public GUIContent Title;
        public GUIContent Subtitle;
    }

    /// <summary>
    /// The menu's view of every setting. Built once while the game loads and kept; rebuilt only
    /// when the set of settings changes (see StockManager.Signature). Values are kept current
    /// through each config file's SettingChanged event, open or closed. Tracks which settings
    /// differ from their default, which keybinds collide, and the undo history.
    /// </summary>
    internal sealed class SettingsModel
    {
        public readonly List<Page> Pages = new List<Page>();
        public readonly Dictionary<string, Page> PagesByGuid = new Dictionary<string, Page>();
        public readonly List<Row> AllRows = new List<Row>();
        public readonly Dictionary<string, Row> RowsById = new Dictionary<string, Row>();
        public readonly UndoHistory<Row> History = new UndoHistory<Row>();

        /// <summary>Rows that share a keybind with at least one other row, mapped to the others.</summary>
        public readonly Dictionary<Row, List<Row>> Conflicts = new Dictionary<Row, List<Row>>();

        public readonly List<List<Row>> ConflictGroups = new List<List<Row>>();
        public readonly List<Row> KeybindRows = new List<Row>();
        public readonly List<Row> ChangedRows = new List<Row>();
        public int ChangedCount;

        private readonly Dictionary<ConfigEntryBase, Row> _rowsByEntry = new Dictionary<ConfigEntryBase, Row>();
        private readonly HashSet<ConfigFile> _watched = new HashSet<ConfigFile>();
        private readonly StockManager _stock;
        private bool _countsDirty = true;
        private bool _keybindsDirty = true;

        /// <summary>
        /// Changes are only recorded for undo while this is above zero: while the menu is drawing
        /// (so a control, or a mod's own drawer, made the change) or during a menu operation such
        /// as pasting settings. A mod that writes its own config on a timer stays out of the history.
        /// </summary>
        public int RecordDepth;

        /// <summary>Goes up whenever the rows, what changed, or the keybinds were worked out again.</summary>
        public int Version;

        public bool Suppress;

        public SettingsModel(StockManager stock)
        {
            _stock = stock;
        }

        /// <summary>Pages that only "Show hidden" lists.</summary>
        public int HiddenPageCount;

        public void Build(IEnumerable<SettingEntryBase> settings, IEnumerable<SettingEntryBase> extra, IEnumerable<BaseUnityPlugin> withoutSettings)
        {
            Pages.Clear();
            PagesByGuid.Clear();
            AllRows.Clear();
            RowsById.Clear();
            _rowsByEntry.Clear();
            // The rows undo points at are being replaced, so their history goes with them. This
            // only happens when the set of settings changed, not on every open.
            History.Clear();

            HashSet<SettingEntryBase> extras = new HashSet<SettingEntryBase>(extra);
            foreach (SettingEntryBase setting in settings.Concat(extras))
            {
                if (setting == null || setting.PluginInfo == null)
                {
                    continue;
                }
                Page page = PageFor(setting.PluginInfo, setting.PluginInstance);
                Row row = MakeRow(setting, page);
                row.IsHidden = setting.Browsable == false
                               || setting.PluginInstance != null && StockManager.IsHidden(setting.PluginInstance.GetType());
                if (extras.Contains(setting))
                {
                    // The only extra row is the stock menu's own hotkey; see Row.IsMenuHotkey.
                    row.IsMenuHotkey = true;
                    row.CanReset = false;
                    row.RefreshChanged();
                }
                page.Rows.Add(row);
                AllRows.Add(row);
                if (row.Entry != null)
                {
                    _rowsByEntry[row.Entry] = row;
                    Watch(row.Entry.ConfigFile);
                    if (row.Id != null)
                    {
                        RowsById[row.Id] = row;
                    }
                }
            }

            foreach (Page page in Pages)
            {
                // Sections keep the order the mod bound them in, as the stock menu does; settings
                // within a section follow the mod's Order, then their name.
                List<string> sectionOrder = page.Rows.Select(r => r.Section).Distinct().ToList();
                foreach (string name in sectionOrder)
                {
                    Section section = new Section { Name = name, Label = new GUIContent(name.ToUpperInvariant()) };
                    section.Rows.AddRange(page.Rows
                        .Where(r => r.Section == name)
                        .OrderByDescending(r => r.Setting.Order)
                        .ThenBy(r => r.Name.text, StringComparer.OrdinalIgnoreCase));
                    page.Sections.Add(section);
                }
                page.Rows.Clear();
                foreach (Section section in page.Sections)
                {
                    page.Rows.AddRange(section.Rows);
                }
            }

            foreach (BaseUnityPlugin plugin in withoutSettings)
            {
                if (plugin != null && plugin.Info != null && plugin.Info.Metadata != null)
                {
                    PageFor(plugin.Info.Metadata, plugin).HasNoSettings = true;
                }
            }

            HiddenPageCount = 0;
            foreach (Page page in Pages)
            {
                page.IsHidden = page.HasNoSettings || page.Rows.All(r => r.IsHidden);
                if (page.IsHidden)
                {
                    HiddenPageCount++;
                }
            }

            Pages.Sort((a, b) => string.CompareOrdinal(a.SortKey, b.SortKey));
            Version++;
            _countsDirty = true;
            _keybindsDirty = true;
        }

        private Page PageFor(BepInPlugin info, BaseUnityPlugin plugin)
        {
            if (PagesByGuid.TryGetValue(info.GUID, out Page page))
            {
                return page;
            }

            string display = DisplayNames.Pretty(info.Name, info.GUID);
            page = new Page
            {
                Guid = info.GUID,
                DisplayName = display,
                AutoName = display,
                OriginalName = info.Name ?? string.Empty,
                SortKey = DisplayNames.SortKey(display),
                Version = info.Version == null ? string.Empty : info.Version.ToString(),
                Author = DisplayNames.AuthorFromGuid(info.GUID) ?? DisplayNames.AuthorFromName(info.Name),
                Plugin = plugin,
                Website = WebsiteOf(plugin),
            };
            page.Title = new GUIContent(display);
            page.BaseSubtitle = "v" + page.Version + (page.Author != null ? "  \u00B7  " + page.Author : string.Empty) + "  \u00B7  " + info.GUID;
            page.Subtitle = new GUIContent(page.BaseSubtitle);
            page.SidebarLabel = new GUIContent(display);
            PagesByGuid[info.GUID] = page;
            Pages.Add(page);
            return page;
        }

        private Row MakeRow(SettingEntryBase setting, Page page)
        {
            ConfigEntryBase entry = _stock.EntryOf(setting);
            string name = (setting.DispName ?? string.Empty).TrimStart('!');
            string section = setting.Category ?? string.Empty;
            Row row = new Row
            {
                Setting = setting,
                Entry = entry,
                Page = page,
                Section = section,
                Name = new GUIContent(name, setting.Description),
                NamePlain = new GUIContent(name),
                Description = string.IsNullOrEmpty(setting.Description) ? null : setting.Description.Trim(),
                ControlName = "bbeRow" + AllRows.Count,
                IsAdvanced = setting.IsAdvanced == true,
                IsReadOnly = setting.ReadOnly == true,
                Id = entry == null ? null : SettingsText.MakeId(page.Guid, entry.Definition.Section, entry.Definition.Key),
            };
            row.CanReset = !setting.HideDefaultButton && (setting.DefaultValue != null || setting.SettingType.IsClass)
                           && !row.IsReadOnly;
            row.DescriptionContent = row.Description == null ? null : new GUIContent(row.Description);
            row.Haystack = SearchQuery.Haystack(page.DisplayName, page.OriginalName, page.Guid, page.Author, section, name, setting.Description);
            row.BaseHaystack = row.Haystack;
            row.Kind = Classify(setting, row);
            row.Category = CategoryOf(row.Kind);
            row.InitDefault();
            return row;
        }

        private RowKind Classify(SettingEntryBase setting, Row row)
        {
            Type type = setting.SettingType;
            if (setting.CustomDrawer != null || setting.CustomHotkeyDrawer != null)
            {
                return RowKind.Stock;
            }
            if (type == typeof(bool))
            {
                return RowKind.Bool;
            }
            if (type == typeof(KeyboardShortcut))
            {
                return RowKind.Shortcut;
            }
            if (type == typeof(KeyCode))
            {
                if (setting.AcceptableValues != null && setting.AcceptableValues.Length > 0)
                {
                    row.Options = setting.AcceptableValues;
                }
                return RowKind.KeyCode;
            }
            if (setting.AcceptableValueRange.Key != null && IsNumber(type))
            {
                return RowKind.Range;
            }
            if (setting.AcceptableValues != null && setting.AcceptableValues.Length > 0
                && type.IsInstanceOfType(setting.AcceptableValues.FirstOrDefault(v => v != null)))
            {
                SetOptions(row, setting.AcceptableValues);
                return RowKind.List;
            }
            if (type.IsEnum)
            {
                if (type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0)
                {
                    return RowKind.Flags;
                }
                SetOptions(row, Enum.GetValues(type).Cast<object>().ToArray());
                return RowKind.Enum;
            }
            if (type == typeof(Color))
            {
                return RowKind.Color;
            }
            if (type == typeof(string))
            {
                return RowKind.Text;
            }
            if (IsNumber(type))
            {
                return RowKind.Number;
            }
            if (_stock.HasStockHandler(type))
            {
                return RowKind.Stock;
            }
            if (setting.ObjToStr != null && setting.StrToObj != null)
            {
                return RowKind.Converted;
            }
            return RowKind.Stock;
        }

        private static RowCategory CategoryOf(RowKind kind)
        {
            switch (kind)
            {
                case RowKind.Bool:
                    return RowCategory.Toggle;
                case RowKind.Range:
                case RowKind.Number:
                    return RowCategory.Number;
                case RowKind.Enum:
                case RowKind.Flags:
                case RowKind.List:
                    return RowCategory.List;
                case RowKind.Shortcut:
                case RowKind.KeyCode:
                    return RowCategory.Keybind;
                case RowKind.Text:
                case RowKind.Converted:
                    return RowCategory.Text;
                default:
                    return RowCategory.Other;
            }
        }

        private static void SetOptions(Row row, object[] options)
        {
            row.Options = options;
            row.OptionLabels = options.Select(o => new GUIContent(OptionText(o))).ToArray();
        }

        /// <summary>Enum values show their [Description] if they have one, otherwise a spaced-out name.</summary>
        public static string OptionText(object value)
        {
            if (value == null)
            {
                return "(none)";
            }
            if (value is Enum)
            {
                System.Reflection.MemberInfo member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
                if (member != null
                    && member.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
                        .FirstOrDefault() is System.ComponentModel.DescriptionAttribute description)
                {
                    return description.Description;
                }
                return SpaceOut(value.ToString());
            }
            return value.ToString();
        }

        private static string SpaceOut(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsNumber(Type type)
        {
            return type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long)
                   || type == typeof(short) || type == typeof(byte) || type == typeof(uint) || type == typeof(ulong)
                   || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(decimal);
        }

        public static bool IsWholeNumber(Type type)
        {
            return IsNumber(type) && type != typeof(float) && type != typeof(double) && type != typeof(decimal);
        }

        /// <summary>Per DLL path, so the version info is read once per session and not on every open.</summary>
        private static readonly Dictionary<string, string> WebsiteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string WebsiteOf(BaseUnityPlugin plugin)
        {
            string location = plugin == null || plugin.Info == null ? null : plugin.Info.Location;
            if (string.IsNullOrEmpty(location))
            {
                return null;
            }
            if (WebsiteCache.TryGetValue(location, out string cached))
            {
                return cached;
            }

            // The same rule as the stock menu: the first well formed absolute URL among the DLL's
            // version-info strings.
            string found = null;
            try
            {
                if (File.Exists(location))
                {
                    FileVersionInfo v = FileVersionInfo.GetVersionInfo(location);
                    foreach (string s in new[] { v.CompanyName, v.FileDescription, v.Comments, v.LegalCopyright, v.LegalTrademarks })
                    {
                        if (!string.IsNullOrEmpty(s) && Uri.IsWellFormedUriString(s, UriKind.Absolute))
                        {
                            found = s;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            WebsiteCache[location] = found;
            return found;
        }

        private void Watch(ConfigFile file)
        {
            if (file != null && _watched.Add(file))
            {
                file.SettingChanged += OnSettingChanged;
            }
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (e == null || e.ChangedSetting == null || !_rowsByEntry.TryGetValue(e.ChangedSetting, out Row row))
            {
                return;
            }
            object now = row.Setting.Get();
            if (RecordDepth > 0 && !Suppress)
            {
                History.Record(row, row.LastValue, now, Time.realtimeSinceStartup, row.Name.text);
            }
            row.LastValue = now;
            row.RefreshChanged();
            _countsDirty = true;
            if (row.Kind == RowKind.Shortcut || row.Kind == RowKind.KeyCode)
            {
                _keybindsDirty = true;
            }
        }

        /// <summary>
        /// Rows without a config entry (the stock debug rows) raise no event, so the menu calls
        /// this after drawing one.
        /// </summary>
        public void Touch(Row row)
        {
            bool was = row.IsChanged;
            row.RefreshChanged();
            if (was != row.IsChanged)
            {
                _countsDirty = true;
            }
        }

        private bool _countedHidden;

        /// <summary>
        /// Recounts what changed and rebuilds the keybind list when something did. Hidden rows
        /// count only while they are shown, so a mod's private window position does not turn up
        /// as a "changed setting" or a key conflict nobody can see.
        /// </summary>
        public void RefreshIfDirty(bool showHidden)
        {
            if (showHidden != _countedHidden)
            {
                _countedHidden = showHidden;
                _countsDirty = true;
                _keybindsDirty = true;
            }
            if (_countsDirty)
            {
                _countsDirty = false;
                Version++;
                ChangedCount = 0;
                ChangedRows.Clear();
                foreach (Page page in Pages)
                {
                    page.ChangedCount = 0;
                    page.VisibleCount = 0;
                    foreach (Row row in page.Rows)
                    {
                        if (row.IsHidden && !showHidden)
                        {
                            continue;
                        }
                        if (row.IsChanged)
                        {
                            page.ChangedCount++;
                            ChangedRows.Add(row);
                        }
                        if (showHidden || !row.IsAdvanced)
                        {
                            page.VisibleCount++;
                        }
                    }
                    ChangedCount += page.ChangedCount;
                }
            }
            if (_keybindsDirty)
            {
                _keybindsDirty = false;
                Version++;
                RebuildKeybinds(showHidden);
            }
        }

        public void MarkCountsDirty() => _countsDirty = true;

        private void RebuildKeybinds(bool showHidden)
        {
            KeybindRows.Clear();
            Conflicts.Clear();
            ConflictGroups.Clear();

            List<HotkeyConflicts.Binding<Row>> bindings = new List<HotkeyConflicts.Binding<Row>>();
            foreach (Row row in AllRows)
            {
                if (row.IsHidden && !showHidden)
                {
                    continue;
                }
                if (row.Kind != RowKind.Shortcut && row.Kind != RowKind.KeyCode
                    && row.Setting.SettingType != typeof(KeyboardShortcut) && row.Setting.SettingType != typeof(KeyCode))
                {
                    continue;
                }
                KeybindRows.Add(row);
                object value = row.Setting.Get();
                if (value is KeyboardShortcut shortcut)
                {
                    bindings.Add(new HotkeyConflicts.Binding<Row>
                    {
                        Owner = row,
                        MainKey = (int)shortcut.MainKey,
                        Modifiers = shortcut.Modifiers.Select(k => (int)k).ToArray(),
                    });
                }
                else if (value is KeyCode key)
                {
                    bindings.Add(new HotkeyConflicts.Binding<Row> { Owner = row, MainKey = (int)key, Modifiers = new int[0] });
                }
            }

            foreach (List<HotkeyConflicts.Binding<Row>> group in HotkeyConflicts.Find(bindings))
            {
                List<Row> rows = group.Select(b => b.Owner).ToList();
                ConflictGroups.Add(rows);
                foreach (Row row in rows)
                {
                    Conflicts[row] = rows.Where(r => r != row).ToList();
                }
            }
        }

        /// <summary>Writes values back for undo, redo, reset or a paste, as one undo step when grouped.</summary>
        public void Write(Row row, object value)
        {
            if (row.IsReadOnly)
            {
                return;
            }
            try
            {
                row.Setting.Set(value);
            }
            catch (Exception e)
            {
                BetterBepInExPlugin.Log.LogWarning("Could not set " + row.Page.DisplayName + " / " + row.Name.text + ": " + e.Message);
            }
            if (row.Entry == null)
            {
                Touch(row);
            }
        }

        public bool Undo(out string label) => Apply(History.Undo(out label));

        public bool Redo(out string label) => Apply(History.Redo(out label));

        private bool Apply(List<KeyValuePair<Row, object>> writes)
        {
            if (writes == null)
            {
                return false;
            }
            Suppress = true;
            try
            {
                foreach (KeyValuePair<Row, object> w in writes)
                {
                    Write(w.Key, w.Value);
                }
            }
            finally
            {
                Suppress = false;
            }
            return true;
        }
    }
}
