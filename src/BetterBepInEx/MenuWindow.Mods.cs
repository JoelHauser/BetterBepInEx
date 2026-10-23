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
    /// The order of the mods list, and which mods were opened last (kept in a hidden config entry,
    /// so it survives restarts). Mods are always listed under their own names: renaming and hiding
    /// them was taken out, so nobody can lose track of a mod.
    /// </summary>
    internal sealed partial class MenuWindow
    {
        private const int MaxRecent = 40;
        private readonly List<string> _recent = new List<string>();
        private readonly Dictionary<string, int> _recentIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly List<Page> _sortedPages = new List<Page>();
        private bool _sortDirty = true;
        private ModSort _sortedBy;
        private int _sortedChanged = -1;
        private int _hiddenListCount;

        private void LoadRecent()
        {
            _recent.Clear();
            foreach (string guid in (_plugin.RecentMods.Value ?? string.Empty).Split('|'))
            {
                if (guid.Length > 0 && !_recent.Contains(guid) && _recent.Count < MaxRecent)
                {
                    _recent.Add(guid);
                }
            }
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

        private static bool ListHidden(Page page) => page.IsHidden;

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

    }
}
