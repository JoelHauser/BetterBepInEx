using System;
using System.Collections.Generic;
using System.ComponentModel;
using BetterBepInEx.Core;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>How the sidebar orders mods. The descriptions are what the settings dropdown shows.</summary>
    public enum ModSort
    {
        [Description("Name")]
        Name,

        [Description("Most changed")]
        MostChanged,

        [Description("Most settings")]
        MostSettings,

        [Description("Recently opened")]
        RecentlyOpened,

        [Description("Author")]
        Author,
    }

    /// <summary>
    /// The player's own arrangement of the mods list: its sort order, names they gave mods, mods
    /// they hid from the list, and which mods they opened last. Names, hidden mods and recents are
    /// kept in hidden config entries, so they survive restarts without a file of their own.
    /// </summary>
    internal sealed partial class MenuWindow
    {
        private const int MaxRecent = 40;
        private const string RenameControl = "bbeRename";

        private readonly Dictionary<string, string> _customNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _userHidden = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _recent = new List<string>();
        private readonly Dictionary<string, int> _recentIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly List<Page> _sortedPages = new List<Page>();
        private bool _sortDirty = true;
        private ModSort _sortedBy;
        private int _sortedChanged = -1;
        private int _hiddenListCount;

        private Page _renaming;
        private string _renameBuffer = string.Empty;
        private bool _focusRename;

        private void LoadCustomizations()
        {
            _customNames.Clear();
            foreach (string line in (_plugin.RenamedMods.Value ?? string.Empty).Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && tab < line.Length - 1)
                {
                    _customNames[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }
            _userHidden.Clear();
            foreach (string guid in (_plugin.HiddenMods.Value ?? string.Empty).Split('|'))
            {
                if (guid.Length > 0)
                {
                    _userHidden.Add(guid);
                }
            }
            _recent.Clear();
            foreach (string guid in (_plugin.RecentMods.Value ?? string.Empty).Split('|'))
            {
                if (guid.Length > 0 && !_recent.Contains(guid) && _recent.Count < MaxRecent)
                {
                    _recent.Add(guid);
                }
            }
        }

        /// <summary>Puts the player's names and hidden flags onto freshly built pages.</summary>
        private void ApplyCustomizations()
        {
            foreach (Page page in _model.Pages)
            {
                page.UserHidden = _userHidden.Contains(page.Guid);
                _customNames.TryGetValue(page.Guid, out string name);
                ApplyName(page, name);
            }
            _sortDirty = true;
        }

        private void ApplyName(Page page, string name)
        {
            string custom = string.IsNullOrEmpty(name) || name.Trim().Length == 0 || name.Trim() == page.AutoName ? null : name.Trim();
            string display = custom ?? page.AutoName;
            page.CustomName = custom;
            page.DisplayName = display;
            page.Title.text = display;
            page.SidebarLabel.text = display;
            page.SortKey = DisplayNames.SortKey(display);
            page.Subtitle.text = custom == null ? page.BaseSubtitle : "Renamed from " + page.AutoName + "  ·  " + page.BaseSubtitle;
            page.SidebarHaystack = null;

            // Search finds a renamed mod's settings by its new name as well as its real one.
            string extra = custom == null ? null : custom.ToLowerInvariant();
            foreach (Row row in page.Rows)
            {
                row.Haystack = extra == null ? row.BaseHaystack : extra + "\n" + row.BaseHaystack;
            }
            _conflictLabels.Clear();
            _searchDirty = true;
            _searchResultsFor = null;
            _listStamp++;
            _sortDirty = true;
        }

        private void StartRename(Page page)
        {
            CommitEdit();
            _renaming = page;
            _renameBuffer = page.DisplayName;
            _focusRename = true;
        }

        private void CommitRename()
        {
            Page page = _renaming;
            _renaming = null;
            if (page == null)
            {
                return;
            }
            ApplyName(page, _renameBuffer);
            if (page.CustomName == null)
            {
                _customNames.Remove(page.Guid);
            }
            else
            {
                _customNames[page.Guid] = page.CustomName;
            }
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in _customNames)
            {
                lines.Add(kv.Key + "\t" + kv.Value.Replace('\n', ' ').Replace('\t', ' '));
            }
            _plugin.RenamedMods.Value = string.Join("\n", lines.ToArray());
            SetStatus(page.CustomName == null ? "Back to its own name." : "Renamed. This only changes how it is listed here.");
        }

        private void ToggleUserHidden(Page page)
        {
            page.UserHidden = !page.UserHidden;
            if (page.UserHidden)
            {
                _userHidden.Add(page.Guid);
                SetStatus(page.DisplayName + " is hidden from the mods list. \"Show hidden mods\" lists it again.");
            }
            else
            {
                _userHidden.Remove(page.Guid);
                SetStatus(page.DisplayName + " is back in the mods list.");
            }
            _plugin.HiddenMods.Value = string.Join("|", new List<string>(_userHidden).ToArray());
            _sortDirty = true;
        }

        /// <summary>Recorded on each visit; the order only takes effect the next time the menu opens, so the list does not jump under the cursor.</summary>
        private void NoteOpened(string guid)
        {
            _recent.Remove(guid);
            _recent.Insert(0, guid);
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }
        }

        private void SaveRecent()
        {
            _plugin.RecentMods.Value = string.Join("|", _recent.ToArray());
        }

        private static bool ListHidden(Page page) => page.IsHidden || page.UserHidden;

        /// <summary>
        /// The pages in the chosen order. Re-sorted only on a Layout event, so the order cannot
        /// change between an event and its Repaint.
        /// </summary>
        private List<Page> SortedPages()
        {
            ModSort sort = _plugin.SortMods.Value;
            bool stale = _sortDirty || sort != _sortedBy || _sortedPages.Count != _model.Pages.Count
                         || sort == ModSort.MostChanged && _model.ChangedCount != _sortedChanged;
            if (!stale || Event.current.type != EventType.Layout && _sortedPages.Count > 0)
            {
                return _sortedPages;
            }

            _sortDirty = false;
            _sortedBy = sort;
            _sortedChanged = _model.ChangedCount;
            _sortedPages.Clear();
            _sortedPages.AddRange(_model.Pages);
            _hiddenListCount = 0;
            foreach (Page page in _sortedPages)
            {
                if (ListHidden(page))
                {
                    _hiddenListCount++;
                }
            }

            switch (sort)
            {
                case ModSort.MostChanged:
                    _sortedPages.Sort((a, b) => Tie(b.ChangedCount.CompareTo(a.ChangedCount), a, b));
                    break;
                case ModSort.MostSettings:
                    _sortedPages.Sort((a, b) => Tie(b.Rows.Count.CompareTo(a.Rows.Count), a, b));
                    break;
                case ModSort.RecentlyOpened:
                    _recentIndex.Clear();
                    for (int i = 0; i < _recent.Count; i++)
                    {
                        _recentIndex[_recent[i]] = i;
                    }
                    _sortedPages.Sort((a, b) => Tie(RecentRank(a).CompareTo(RecentRank(b)), a, b));
                    break;
                case ModSort.Author:
                    _sortedPages.Sort((a, b) => Tie(string.Compare(AuthorKey(a), AuthorKey(b), StringComparison.OrdinalIgnoreCase), a, b));
                    break;
                default:
                    _sortedPages.Sort((a, b) => string.CompareOrdinal(a.SortKey, b.SortKey));
                    break;
            }
            return _sortedPages;
        }

        private static int Tie(int primary, Page a, Page b) => primary != 0 ? primary : string.CompareOrdinal(a.SortKey, b.SortKey);

        private int RecentRank(Page page) => _recentIndex.TryGetValue(page.Guid, out int i) ? i : int.MaxValue;

        /// <summary>Mods with no known author sort after every named one.</summary>
        private static string AuthorKey(Page page) => string.IsNullOrEmpty(page.Author) ? "￿" : page.Author;

        /// <summary>The mod page's title, or the rename box in its place while renaming.</summary>
        private void DrawPageTitle(Page page)
        {
            if (_renaming != page)
            {
                Header(page.Title, page.Subtitle);
                return;
            }
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(RenameControl);
            _renameBuffer = GUILayout.TextField(_renameBuffer ?? string.Empty, _skin.RenameField, Opts(Opt.MinMaxWidth, U(240), U(460)));
            if (_focusRename)
            {
                GUI.FocusControl(RenameControl);
                _focusRename = false;
            }
            // As tall as the name field, so the row lines up.
            GUILayoutOption[] height = Opts(Opt.NoExpandHeight, _skin.RenameField.fixedHeight);
            if (GUILayout.Button(Cached("Save"), _skin.ButtonAccent, height))
            {
                CommitRename();
            }
            if (GUILayout.Button(Cached("Cancel"), _skin.Ghost, height))
            {
                _renaming = null;
            }
            if (page.CustomName != null && GUILayout.Button(Cached("Use its own name"), _skin.Ghost, height))
            {
                _renameBuffer = string.Empty;
                CommitRename();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Cached("Only changes how this mod is listed here. Leave it empty to go back to its own name."), _skin.PageSubtitle);
        }
    }
}
