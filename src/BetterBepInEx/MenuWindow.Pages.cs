using System;
using System.Collections.Generic;
using BetterBepInEx.Core;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>The filter chips above a list of settings.</summary>
    internal enum RowFilter
    {
        All,
        Changed,
        Toggle,
        Number,
        List,
        Keybind,
        Text,
        Other,
    }

    internal sealed partial class MenuWindow
    {
        private static readonly RowFilter[] Filters =
        {
            RowFilter.All, RowFilter.Changed, RowFilter.Toggle, RowFilter.Number,
            RowFilter.List, RowFilter.Keybind, RowFilter.Text, RowFilter.Other,
        };

        private static readonly GUIContent HiddenTag = new GUIContent("HIDDEN",
            "Its mod hides this setting from the menu. Change it only if you know what it does.");

        private static readonly string[] FilterNames = { "All  ", "Changed  ", "Toggles  ", "Numbers  ", "Lists  ", "Keybinds  ", "Text  ", "Other  " };

        private static readonly GUIContent FavoriteLabel = new GUIContent("Favorite", "Listed under Favorites at the top of the mods list. Click to remove it.");
        private static readonly GUIContent AddFavoriteLabel = new GUIContent("Add to favorites", "List this mod under Favorites at the top of the mods list");
        private static readonly GUIContent WebsiteLabel = new GUIContent("Website", "Open this mod's website");

        /// <summary>What a list was last built from. The same again means the same rows, so it is not built again.</summary>
        private struct ListSource
        {
            public object Of;
            public RowFilter Filter;
            public SearchQuery Query;
            public SearchQuery RowQuery;
            public int Flags;
            public int Model;
            public int Stamp;
        }

        private ListSource _countedFrom;
        private ListSource _listedFrom;

        /// <summary>
        /// True when a list built from <paramref name="of"/> now may differ from the last one built
        /// (which is then noted as this one). Settings changing value, a new search, Show hidden and
        /// renames all show up in the model's version or <see cref="_listStamp"/>.
        /// </summary>
        private bool Stale(ref ListSource last, object of)
        {
            int flags = (ShowHidden ? 1 : 0) | (ShowAdvanced ? 2 : 0);
            if (ReferenceEquals(last.Of, of) && last.Filter == _filter && ReferenceEquals(last.Query, _query) && ReferenceEquals(last.RowQuery, _rowQuery)
                && last.Flags == flags && last.Model == _model.Version && last.Stamp == _listStamp)
            {
                return false;
            }
            last = new ListSource { Of = of, Filter = _filter, Query = _query, RowQuery = _rowQuery, Flags = flags, Model = _model.Version, Stamp = _listStamp };
            return true;
        }

        private const string PageSearch = "@search";

        private RowFilter _filter = RowFilter.All;
        private readonly int[] _filterCounts = new int[8];
        private int _filterHiddenCount;
        private RowFilter _chipsFilter = RowFilter.All;
        private bool _chipsShowHidden;

        private const string ModSearchControl = "bbeModSearch";
        private static readonly GUIContent ModSearchHint = new GUIContent("Search this mod");

        /// <summary>The search box on a mod's page: what is typed there, and the query made of it.</summary>
        private string _modSearch = string.Empty;
        private SearchQuery _modQuery = SearchQuery.Empty;

        /// <summary>While a mod page is drawn and its search box has text: rows must match it to be listed or counted.</summary>
        private SearchQuery _rowQuery;

        private void SetModSearch(string text)
        {
            _modSearch = text ?? string.Empty;
            _modQuery = new SearchQuery(_modSearch);
        }

        private readonly HashSet<Row> _shownInConflicts = new HashSet<Row>();
        private readonly Dictionary<List<Row>, GUIContent> _conflictLabels = new Dictionary<List<Row>, GUIContent>();

        /// <summary>
        /// The page shown, chosen on Layout and kept for the event that follows, so a click that
        /// opens another page or a key typed into the search box cannot change what is drawn
        /// between the two. GUILayout throws when they disagree, and on a Repaint that cuts the
        /// window's drawing short: a flash.
        /// </summary>
        private string _shownPage;

        private void DrawContent()
        {
            if (Event.current.type == EventType.Layout)
            {
                _shownPage = !_query.IsEmpty ? PageSearch : _page;
                if (_shownPage != PageSearch && _shownPage != PageChanged && _shownPage != PageKeybinds
                    && (_shownPage == null || !_model.PagesByGuid.ContainsKey(_shownPage)))
                {
                    _page = PageChanged;
                    _shownPage = PageChanged;
                }
            }
            switch (_shownPage)
            {
                case PageSearch:
                    DrawSearchResults();
                    return;
                case PageChanged:
                    DrawChanged();
                    return;
                case PageKeybinds:
                    DrawKeybinds();
                    return;
            }
            if (_shownPage != null && _model.PagesByGuid.TryGetValue(_shownPage, out Page page))
            {
                DrawPage(page);
                return;
            }
            DrawChanged();
        }

        private void Header(GUIContent title, GUIContent subtitle)
        {
            GUILayout.Label(title, _skin.PageTitle);
            if (subtitle != null)
            {
                GUILayout.Label(subtitle, _skin.PageSubtitle);
            }
        }

        private bool Passes(Row row) => Passes(row, _filter);

        private bool Passes(Row row, RowFilter filter)
        {
            if (row.IsHidden && !ShowHidden || row.IsAdvanced && !ShowAdvanced)
            {
                return false;
            }
            if (_rowQuery != null && !_rowQuery.Matches(row.Haystack))
            {
                return false;
            }
            switch (filter)
            {
                case RowFilter.All:
                    return true;
                case RowFilter.Changed:
                    return row.IsChanged;
                default:
                    return (int)row.Category == (int)filter - 2;
            }
        }

        /// <summary>
        /// The chip bar: one chip per kind of setting present in <paramref name="rows"/>, with its
        /// count, and the advanced toggle on the right.
        /// </summary>
        private void DrawFilterBar(List<Row> rows, bool modSearch = false)
        {
            // Counted on Layout only, and only when the rows may have changed: a search can list
            // thousands of them, and the chips drawn have to stay the same for the event that follows.
            if (Event.current.type == EventType.Layout && Stale(ref _countedFrom, rows))
            {
                Array.Clear(_filterCounts, 0, _filterCounts.Length);
                _filterHiddenCount = 0;
                foreach (Row row in rows)
                {
                    if (_rowQuery != null && !_rowQuery.Matches(row.Haystack))
                    {
                        continue;
                    }
                    // Advanced rows are always listed in search results, so only hidden ones count there.
                    if (row.IsHidden || row.IsAdvanced && _query.IsEmpty)
                    {
                        _filterHiddenCount++;
                    }
                    if (!Passes(row, RowFilter.All))
                    {
                        continue;
                    }
                    _filterCounts[0]++;
                    if (row.IsChanged)
                    {
                        _filterCounts[1]++;
                    }
                    _filterCounts[(int)row.Category + 2]++;
                }
            }
            // Which chips there are is fixed on Layout too. The title bar's and sidebar's switches
            // are drawn first and can flip Show hidden mid-click, and a chip click changes the
            // filter partway through this loop; either would add or drop a chip from that event.
            if (Event.current.type == EventType.Layout)
            {
                _chipsFilter = _filter;
                _chipsShowHidden = ShowHidden;
            }
            int hiddenCount = _filterHiddenCount;

            GUILayout.Space(U(10));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Filters.Length; i++)
            {
                RowFilter f = Filters[i];
                if (_filterCounts[i] == 0 && f != RowFilter.All && f != _chipsFilter)
                {
                    continue;
                }
                bool on = _chipsFilter == f;
                if (GUILayout.Button(CountedLabel(FilterNames[i], _filterCounts[i], 10 + i), on ? _skin.ChipOn : _skin.Chip, NoExpand))
                {
                    _filter = on && f != RowFilter.All ? RowFilter.All : f;
                }
            }
            GUILayout.FlexibleSpace();
            // The switch itself is in the title bar, on every page. Here, only what it would add
            // to this list, where there is something: most mods hide or mark advanced nothing.
            if (hiddenCount > 0 && !_chipsShowHidden)
            {
                GUIContent label = CountedLabel("+", hiddenCount, 22, " advanced");
                label.tooltip = "Settings this list leaves out. Click to turn on Show advanced.";
                if (GUILayout.Button(label, _skin.Chip, NoExpand))
                {
                    ToggleHidden();
                }
            }
            if (modSearch)
            {
                GUILayout.Space(U(6));
                string typed = SearchBox(ModSearchControl, _modSearch, ModSearchHint, _skin.SidebarFilter, out bool cleared, W(U(240)));
                if (typed != _modSearch)
                {
                    SetModSearch(typed);
                }
                if (cleared)
                {
                    GUI.FocusControl(ModSearchControl);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(U(6));
        }

        // ------------------------------------------------------------------ one mod

        private void DrawPage(Page page)
        {
            // The title takes whatever width the buttons leave, and wraps in it. The buttons are
            // all ghost buttons of one height, so they line up.
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Header(page.Title, page.Subtitle);
            GUILayout.EndVertical();
            GUILayout.Space(U(12));

            bool favorite = _favorites.Contains(page.Guid);
            if (GUILayout.Button(favorite ? FavoriteLabel : AddFavoriteLabel, favorite ? _skin.GhostOn : _skin.Ghost, NoExpand))
            {
                ToggleFavorite(page);
            }
            if (page.Website != null && GUILayout.Button(WebsiteLabel, _skin.Ghost, NoExpand))
            {
                Application.OpenURL(page.Website);
            }
            GUI.enabled = page.ChangedCount > 0;
            if (Confirm(page, "Reset mod", "Reset ", page.ChangedCount, 50, _skin.GhostDanger, NoExpand))
            {
                ResetRows(page.Rows, "reset " + page.DisplayName);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // The mod's own search narrows this page's rows, counts and chips, and nothing else.
            _rowQuery = _modQuery.IsEmpty ? null : _modQuery;
            try
            {
                DrawPageList(page);
            }
            finally
            {
                _rowQuery = null;
            }
        }

        private void DrawPageList(Page page)
        {
            DrawFilterBar(page.Rows, true);

            if (BuildingList(page.Guid))
            {
                int listed = 0;
                bool sectionNames = page.Sections.Count > 1 || page.Sections.Count == 1 && page.Sections[0].Name.Length > 0;
                foreach (Section section in page.Sections)
                {
                    listed += AddGroup(section, sectionNames && section.Name.Length > 0 ? section.Label : null, _skin.SectionHeader, null, section.Rows, false);
                }
                if (listed == 0)
                {
                    _listEmpty = page.Rows.Count == 0 ? "This mod has no settings to change. It is listed so you can see everything that is loaded."
                        : _rowQuery != null ? "No setting in this mod matches the search."
                        : _filter != RowFilter.All ? "Nothing here matches the filter."
                        : "This mod's author hides all of its settings or marks them advanced. Turn on Show advanced, at the top of the menu, to see them.";
                }
            }
            DrawList(page.Guid);
        }

        // ------------------------------------------------------------------ search

        private void DrawSearchResults()
        {
            Header(Cached("Search results"), _searchSummary);
            DrawFilterBar(_searchResults);
            if (BuildingList(PageSearch) && AddByPage(_searchResults, null) == 0)
            {
                _listEmpty = _searchResults.Count == 0
                    ? "Nothing matches. Every word has to appear in a mod's name, a setting's name, its section or its description."
                    : "Nothing here matches the filter.";
            }
            DrawList(PageSearch);
        }

        // ------------------------------------------------------------------ changed

        private void DrawChanged()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Header(Cached("Changed settings"), Cached("Every setting that is not at its default, across all mods."));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUI.enabled = _model.ChangedCount > 0;
            if (Confirm(PageChanged, "Reset all", "Reset all ", _model.ChangedCount, 51, _skin.GhostDanger, NoExpand))
            {
                ResetRows(new List<Row>(_model.ChangedRows), "reset every mod");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawFilterBar(_model.ChangedRows);
            if (BuildingList(PageChanged) && AddByPage(_model.ChangedRows, null) == 0)
            {
                _listEmpty = _model.ChangedCount == 0 ? "Every setting is at its default." : "Nothing here matches the filter.";
            }
            DrawList(PageChanged);
        }

        // ------------------------------------------------------------------ keybinds

        private void DrawKeybinds()
        {
            int conflicts = _model.ConflictGroups.Count;
            Header(Cached("Keybinds"), Cached(conflicts == 0
                ? "Every key bound by a mod. No two settings share a key."
                : "Every key bound by a mod. Keys that more than one setting uses come first; every mod bound to one reacts to it."));
            GUILayout.Space(U(6));

            if (BuildingList(PageKeybinds))
            {
                _shownInConflicts.Clear();
                int listed = 0;
                foreach (List<Row> group in _model.ConflictGroups)
                {
                    if (!_conflictLabels.TryGetValue(group, out GUIContent title))
                    {
                        title = new GUIContent(ShortcutText(group[0].Setting.Get()) + "   used by " + group.Count + " settings");
                        _conflictLabels[group] = title;
                    }
                    // Every conflict heading is one line in one style: one height, kept under one
                    // key, not under the group's list, which is a new object each time keys change.
                    listed += AddGroup(ConflictHeadingKey, title, _skin.GroupHeaderDanger, null, group, true);
                    foreach (Row row in group)
                    {
                        _shownInConflicts.Add(row);
                    }
                }
                listed += AddByPage(_model.KeybindRows, _shownInConflicts);
                if (_model.KeybindRows.Count == 0)
                {
                    _listEmpty = "No mod has a keybind setting.";
                }
                else if (listed == 0)
                {
                    _listEmpty = "Nothing here matches the filter.";
                }
            }
            DrawList(PageKeybinds);
        }

        // ------------------------------------------------------------------ lists of rows

        /// <summary>
        /// One entry in a scrolling list: a heading, the gap above an unnamed section, or a
        /// setting's row. Pages list their blocks on Layout (<see cref="BuildingList"/>); the
        /// event that follows draws the same ones.
        /// </summary>
        private struct Block
        {
            /// <summary>What the block's height is kept under: its Row, Page, Section or conflict group. Null for a gap.</summary>
            public object Key;
            public Row Row;
            public GUIContent Header;
            public GUIStyle Style;
            public Page Link;
            public bool PageName;
            public bool First;
            public bool Last;
        }

        private readonly List<Block> _blocks = new List<Block>();
        private string _listEmpty;

        /// <summary>
        /// True on a Layout where the page should list its blocks afresh: the first for this list,
        /// or once anything it shows may have changed. Every other event draws the blocks as they are.
        /// </summary>
        private bool BuildingList(string key)
        {
            if (Event.current.type != EventType.Layout || !Stale(ref _listedFrom, key))
            {
                return false;
            }
            _blocks.Clear();
            _listEmpty = null;
            _blocksChanged = true;
            return true;
        }

        /// <summary>Rows from many mods, grouped under each mod's name, in the order given.</summary>
        private int AddByPage(List<Row> rows, HashSet<Row> skip)
        {
            int listed = 0;
            int start = 0;
            while (start < rows.Count)
            {
                Page page = rows[start].Page;
                int end = start;
                while (end < rows.Count && rows[end].Page == page)
                {
                    end++;
                }
                listed += AddGroup(page, page.Title, _skin.GroupHeader, page, rows, false, start, end, skip);
                start = end;
            }
            return listed;
        }

        /// <summary>
        /// One heading and a card of rows under it. Rows the filter hides are left out, so the
        /// card's rounded top and bottom land on the rows actually shown, and an empty group adds
        /// nothing. Returns the number of rows added.
        /// </summary>
        private int AddGroup(object key, GUIContent header, GUIStyle headerStyle, Page link, List<Row> rows, bool showPageName,
            int start = 0, int end = -1, HashSet<Row> skip = null)
        {
            if (end < 0)
            {
                end = rows.Count;
            }
            int count = 0;
            for (int i = start; i < end; i++)
            {
                Row row = rows[i];
                if (!Passes(row) || skip != null && skip.Contains(row))
                {
                    continue;
                }
                if (count == 0)
                {
                    _blocks.Add(new Block { Key = header == null ? null : key, Header = header, Style = headerStyle, Link = link });
                }
                _blocks.Add(new Block { Key = row, Row = row, PageName = showPageName, First = count == 0 });
                count++;
            }
            if (count > 0)
            {
                Block last = _blocks[_blocks.Count - 1];
                last.Last = true;
                _blocks[_blocks.Count - 1] = last;
            }
            return count;
        }

        private void EmptyNote(string text)
        {
            GUILayout.Space(U(24));
            GUILayout.Label(text, _skin.Muted);
        }

        // ------------------------------------------------------------------ virtualized list

        // IMGUI lays out every control on every event, several events a frame, so only the blocks
        // on screen are drawn; above and below them is one Space each (see Core/VirtualList).
        // Heights are measured when a block is drawn and kept per row, heading or section, across
        // pages and searches, until the layout changes (width, text size, descriptions). A new
        // search therefore draws a screenful of rows, not every match.
        //
        // What is drawn is decided on Layout and kept for the event that follows: GUILayout throws
        // when the two disagree, and on a Repaint that cuts the window's drawing short.
        //
        // The view stays put by remembering, after each Repaint, which row is at its top and how
        // far into it; the next Layout scrolls to that row wherever the new heights put it.

        private const float ListMovingSeconds = 0.2f;

        private static readonly object ConflictHeadingKey = new object();

        private readonly VirtualList _virtual = new VirtualList();
        private readonly Dictionary<object, float> _heights = new Dictionary<object, float>();

        /// <summary>Heights of rows drawn with their mod's name over them (the Keybinds page's conflicts).</summary>
        private readonly Dictionary<object, float> _pagedHeights = new Dictionary<object, float>();

        /// <summary>The blocks, or a height, changed since the positions were last worked out.</summary>
        private bool _blocksChanged;
        private bool _heightsChanged;

        private float _plainRowSum;
        private int _plainRowCount;
        private float _headerSum;
        private int _headerCount;
        private float _descCharsPerLine = 60f;

        private string _listKey;
        private RowFilter _listFilter;
        private SearchQuery _listQuery;
        private SearchQuery _listRowQuery;
        private int _listWidth = -1;
        private int _listFlags = -1;
        private float _listViewHeight;
        private Rect _listViewRect;
        private float _listMaxScroll;
        private float _topSpace;
        private float _bottomSpace;

        /// <summary>Blocks First up to this one are all measured, so where they will land is known before drawing.</summary>
        private int _knownRun;

        private Row _anchorRow;
        private bool _anchorPaged;
        private float _anchorOffset;

        private bool _wheeling;
        private float _wheelTarget;
        private float _listMovedAt = -10f;

        /// <summary>The list scrolled within the last moment; row hover and tooltips wait until it stops.</summary>
        private bool ListMoving => Time.realtimeSinceStartup - _listMovedAt < ListMovingSeconds;

        /// <summary>Forgets every measured height: the rows will lay out differently.</summary>
        private void ResetVirtualization()
        {
            _heights.Clear();
            _pagedHeights.Clear();
            _heightsChanged = true;
            _plainRowSum = 0f;
            _plainRowCount = 0;
            _headerSum = 0f;
            _headerCount = 0;
        }

        private void ArrangeList(string key)
        {
            // What a row's height depends on. Show hidden and search change which rows are listed,
            // not how tall they are.
            int width = Mathf.RoundToInt(_rowWidth);
            int flags = _plugin.ShowDescriptions.Value ? 1 : 0;
            if (width != _listWidth || flags != _listFlags)
            {
                ResetVirtualization();
                _listWidth = width;
                _listFlags = flags;
            }

            _scroll.TryGetValue(key, out Vector2 pos);
            bool sameList = key == _listKey;
            bool newRowQuery = !ReferenceEquals(_rowQuery, _listRowQuery);
            if (sameList && (_filter != _listFilter || newRowQuery) || key == PageSearch && !ReferenceEquals(_query, _listQuery))
            {
                // The same list filtered or searched differently, or a new search: start from the top.
                pos = Vector2.zero;
            }
            if (!sameList || _filter != _listFilter || newRowQuery || !ReferenceEquals(_query, _listQuery))
            {
                _anchorRow = null;
                _wheeling = false;
            }
            _listKey = key;
            _listFilter = _filter;
            _listQuery = _query;
            _listRowQuery = _rowQuery;

            // Positions are worked out again only when a block or a height changed. Otherwise they
            // are as they were, and so is the scroll position in them.
            if (_blocksChanged || _heightsChanged)
            {
                _blocksChanged = false;
                _heightsChanged = false;
                GUIStyle desc = _skin.Description;
                float charWidth = Mathf.Max(4f, (desc.fontSize > 0 ? desc.fontSize : U(13)) * 0.5f);
                _descCharsPerLine = Mathf.Max(10f, (_rowWidth - _valueWidth - U(60)) / charWidth);

                _virtual.Clear();
                int anchor = -1;
                for (int i = 0; i < _blocks.Count; i++)
                {
                    Block b = _blocks[i];
                    _virtual.Add(HeightOf(b, out bool known), known);
                    if (anchor < 0 && _anchorRow != null && b.Row == _anchorRow && b.PageName == _anchorPaged)
                    {
                        anchor = i;
                    }
                }
                if (anchor >= 0)
                {
                    // A wheel scroll under way moves with the rows it is heading for.
                    float y = _virtual.Y(anchor) + _anchorOffset;
                    _wheelTarget += y - pos.y;
                    pos.y = y;
                }
            }

            float view = _listViewHeight > 0f ? _listViewHeight : _rect.height;
            _listMaxScroll = Mathf.Max(0f, _virtual.Total + U(32) - view);
            pos.y = Mathf.Clamp(pos.y, 0f, _listMaxScroll);
            _wheelTarget = Mathf.Clamp(_wheelTarget, 0f, _listMaxScroll);
            _scroll[key] = pos;

            _virtual.Arrange(pos.y, view, Mathf.Max(U(160), view * 0.25f), view, U(36), 8);
            _topSpace = _virtual.TopSpace;
            _bottomSpace = _virtual.BottomSpace;
            _knownRun = _virtual.First;
            while (_knownRun < _virtual.End && _virtual.Known(_knownRun))
            {
                _knownRun++;
            }
        }

        private float HeightOf(Block b, out bool known)
        {
            known = true;
            if (b.Key == null)
            {
                return U(8);
            }
            if ((b.PageName ? _pagedHeights : _heights).TryGetValue(b.Key, out float h))
            {
                return h;
            }
            known = false;
            if (b.Row == null)
            {
                return _headerCount > 0 ? _headerSum / _headerCount : U(44);
            }
            h = _plainRowCount > 0 ? _plainRowSum / _plainRowCount : U(60);
            if (b.PageName)
            {
                h += _skin.Small.lineHeight;
            }
            if (_plugin.ShowDescriptions.Value && b.Row.Description != null)
            {
                h += _skin.Description.lineHeight * Mathf.Ceil(b.Row.Description.Length / _descCharsPerLine) + U(4);
            }
            return h;
        }

        private void StoreHeight(Block b, float h)
        {
            if (b.Key == null)
            {
                return;
            }
            Dictionary<object, float> heights = b.PageName ? _pagedHeights : _heights;
            bool first = !heights.ContainsKey(b.Key);
            heights[b.Key] = h;
            _heightsChanged = true;
            if (!first)
            {
                return;
            }
            // Averages for the rows and headings not drawn yet.
            if (b.Row == null)
            {
                _headerSum += h;
                _headerCount++;
            }
            else if (!b.PageName && (!_plugin.ShowDescriptions.Value || b.Row.Description == null))
            {
                _plainRowSum += h;
                _plainRowCount++;
            }
        }

        /// <summary>The scrolling list of <see cref="_blocks"/>, with the view kept where it was.</summary>
        private void DrawList(string key)
        {
            Event e = Event.current;
            EventType type = e.type;
            if (type == EventType.Layout)
            {
                ArrangeList(key);
            }
            key = _listKey ?? key;
            _scroll.TryGetValue(key, out Vector2 pos);

            // The wheel eases towards where it points rather than jumping a notch at a time.
            if (type == EventType.ScrollWheel && e.delta.y != 0f && _listViewRect.Contains(e.mousePosition))
            {
                _wheelTarget = Mathf.Clamp((_wheeling ? _wheelTarget : pos.y) + e.delta.y * U(20), 0f, _listMaxScroll);
                _wheeling = true;
                _listMovedAt = Time.realtimeSinceStartup;
                e.Use();
            }

            Vector2 shown = GUILayout.BeginScrollView(pos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            GUILayout.BeginVertical(W(_rowWidth));
            GUILayout.Space(_topSpace);
            for (int i = _virtual.First; i < _virtual.End; i++)
            {
                DrawBlock(i, false);
            }
            GUILayout.Space(_bottomSpace);
            if (_listEmpty != null)
            {
                EmptyNote(_listEmpty);
            }
            GUILayout.Space(U(32));
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            if (type == EventType.Repaint)
            {
                _listViewRect = GUILayoutUtility.GetLastRect();
                _listViewHeight = _listViewRect.height;
            }

            if (_virtual.Offscreen.Count > 0)
            {
                // Rows about to scroll into view from above, measured where nobody can see or click them.
                GUILayout.BeginArea(new Rect(-100000f, 0f, _rowWidth, 1f));
                GUILayout.BeginVertical(W(_rowWidth));
                foreach (int i in _virtual.Offscreen)
                {
                    DrawBlock(i, true);
                }
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }

            if (type == EventType.Repaint)
            {
                if (_wheeling)
                {
                    float step = 1f - Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 16f);
                    pos.y = Mathf.Abs(_wheelTarget - pos.y) < 0.5f ? _wheelTarget : pos.y + (_wheelTarget - pos.y) * step;
                    _wheeling = pos.y != _wheelTarget;
                    _listMovedAt = Time.realtimeSinceStartup;
                }
                RememberAnchor(pos.y);
            }
            else if (type != EventType.Layout && shown != pos)
            {
                // Dragged the scrollbar: that wins over the wheel, and over the remembered row.
                pos = shown;
                _wheeling = false;
                _anchorRow = null;
                _listMovedAt = Time.realtimeSinceStartup;
            }
            _scroll[key] = pos;
        }

        private void DrawBlock(int i, bool offscreen)
        {
            Block b = _blocks[i];
            if (b.Key == null)
            {
                GUILayout.Space(U(8));
            }
            else if (b.Row != null)
            {
                Rect known = !offscreen && i < _knownRun ? new Rect(0f, _virtual.Y(i), _rowWidth, _virtual.Height(i)) : default;
                _drawingOffscreen = offscreen;
                try
                {
                    DrawRow(b.Row, b.PageName, b.First, b.Last, offscreen, known);
                }
                finally
                {
                    _drawingOffscreen = false;
                }
            }
            else if (b.Link != null)
            {
                if (GUILayout.Button(b.Header, b.Style, Expand) && !offscreen)
                {
                    Select(b.Link.Guid);
                }
            }
            else
            {
                GUILayout.Label(b.Header, b.Style, Expand);
            }

            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                if (!_virtual.Known(i) || r.height != _virtual.Height(i))
                {
                    StoreHeight(b, r.height);
                }
                if (!offscreen)
                {
                    _virtual.Shown(i, r.y, r.height);
                }
            }
        }

        /// <summary>
        /// Notes the row at the top of the view and how far into it, as drawn this Repaint, so the
        /// next Layout can keep it there whatever heights change above it.
        /// </summary>
        private void RememberAnchor(float scroll)
        {
            _anchorRow = null;
            if (scroll <= 0f)
            {
                return;
            }
            int i = _virtual.IndexAt(scroll);
            if (i < 0)
            {
                return;
            }
            // Headings can repeat (a mod's name heads each run of its keybinds), so the anchor is
            // the nearest row: the one in view, or the first one below a heading.
            int row = i;
            while (row < _blocks.Count && _blocks[row].Row == null)
            {
                row++;
            }
            if (row == _blocks.Count)
            {
                row = i;
                while (row >= 0 && _blocks[row].Row == null)
                {
                    row--;
                }
                if (row < 0)
                {
                    return;
                }
            }
            _anchorRow = _blocks[row].Row;
            _anchorPaged = _blocks[row].PageName;
            _anchorOffset = scroll - _virtual.DisplayedTop(row);
        }

        private void ResetRows(List<Row> rows, string label)
        {
            CommitEdit();
            int n = 0;
            _model.History.BeginGroup(label);
            try
            {
                foreach (Row row in rows)
                {
                    if (row.IsChanged && row.CanReset)
                    {
                        _model.Write(row, row.Setting.DefaultValue);
                        n++;
                    }
                }
            }
            finally
            {
                _model.History.EndGroup();
            }
            SetStatus("Reset " + n + Plural(n, " setting", " settings") + ". Ctrl+Z undoes it.");
        }

        // ------------------------------------------------------------------ one setting

        private GUIStyle CardStyle(bool first, bool last)
        {
            return first && last ? _skin.CardSingle : first ? _skin.CardTop : last ? _skin.CardBottom : _skin.CardMid;
        }

        /// <summary>
        /// One setting: name, notes and description on the left, the control in a fixed-width
        /// column on the right, so controls line up down the page whatever the left side holds.
        /// The card shape comes from the row's place in its group.
        /// </summary>
        /// <param name="offscreen">Drawn out of sight only to be measured: no decorations.</param>
        /// <param name="known">Where the row will land, when its height and those above it are measured; empty otherwise.</param>
        private void DrawRow(Row row, bool showPageName, bool first, bool last, bool offscreen, Rect known)
        {
            GUILayout.BeginHorizontal(CardStyle(first, last));
            bool repaint = Event.current.type == EventType.Repaint && !offscreen;
            if (repaint && known.width > 0f && !ListMoving && known.Contains(Event.current.mousePosition))
            {
                // The hover goes under the row's text, so it needs the rect before the row is
                // laid out. Rows only light up once the list stops: a list scrolling under a
                // still cursor would flicker every row it passes.
                _skin.RowHover.Draw(new Rect(known.x + U(3), known.y + U(3), known.width - U(6), known.height - U(6)), false, false, false, false);
            }

            GUILayout.BeginVertical();
            if (showPageName)
            {
                GUILayout.Label(row.Page.Title, _skin.Small);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(_plugin.ShowDescriptions.Value ? row.NamePlain : row.Name, _skin.Label);
            // While its slider is held, the row keeps the Reset link it had when the drag began:
            // one appearing mid-drag would move the slider under the mouse.
            bool showReset = _slideRow == row ? _slideShowReset : row.IsChanged && row.CanReset;
            if (showReset)
            {
                if (row.ResetLabel == null)
                {
                    row.ResetLabel = new GUIContent("Reset", "Back to " + DefaultText(row));
                }
                if (GUILayout.Button(row.ResetLabel, _skin.LinkButton, NoExpand))
                {
                    CommitEdit();
                    _model.Write(row, row.Setting.DefaultValue);
                }
            }
            GUILayout.EndHorizontal();
            DrawTags(row);
            if (_plugin.ShowDescriptions.Value && row.DescriptionContent != null)
            {
                GUILayout.Label(row.DescriptionContent, _skin.Description);
            }
            GUILayout.EndVertical();

            GUILayout.Space(U(20));
            GUILayout.BeginVertical(W(_valueWidth));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !row.IsReadOnly;
            if (_failedRows.Contains(row))
            {
                GUILayout.Label("This setting failed to draw. The BepInEx log has the error.", _skin.Small);
            }
            else
            {
                try
                {
                    DrawValue(row);
                }
                catch (ExitGUIException)
                {
                    throw;
                }
                catch (Exception e) when (!(e is ArgumentException))
                {
                    _failedRows.Add(row);
                    BetterBepInExPlugin.Log.LogError("Failed to draw " + row.Page.DisplayName + " / " + row.Name.text + ": " + e);
                }
            }
            GUI.enabled = enabled;
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (repaint)
            {
                // The divider and the changed bar sit in the card's padding, clear of the text, so
                // they are drawn after it, in the rect the row really got this frame.
                Rect r = GUILayoutUtility.GetLastRect();
                if (!first)
                {
                    _skin.Fill(new Rect(r.x + U(16), r.y, r.width - U(32), 1), Skin.Divider);
                }
                bool conflict = _model.Conflicts.ContainsKey(row);
                if (row.IsChanged || conflict)
                {
                    float h = Mathf.Max(U(16), r.height - U(28));
                    _skin.Fill(new Rect(r.x + U(5), r.y + (r.height - h) / 2f, U(3), h), conflict ? Skin.Danger : Skin.Accent);
                }
            }
        }

        private void DrawTags(Row row)
        {
            bool advanced = row.IsAdvanced;
            bool readOnly = row.IsReadOnly;
            bool conflict = _model.Conflicts.TryGetValue(row, out List<Row> others);
            if (advanced || readOnly || row.IsHidden)
            {
                GUILayout.BeginHorizontal();
                if (row.IsHidden)
                {
                    GUILayout.Label(HiddenTag, _skin.Tag, NoExpand);
                }
                if (advanced)
                {
                    GUILayout.Label(Cached("ADVANCED"), _skin.Tag, NoExpand);
                }
                if (readOnly)
                {
                    GUILayout.Label(Cached("READ ONLY"), _skin.Tag, NoExpand);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (conflict)
            {
                if (!_conflictLabels.TryGetValue(others, out GUIContent note))
                {
                    Row first = others[0];
                    note = new GUIContent("Same key as " + first.Page.DisplayName + " › " + first.Name.text
                                          + (others.Count > 1 ? "  (+" + (others.Count - 1) + " more)" : string.Empty),
                        "Open the Keybinds page");
                    _conflictLabels[others] = note;
                }
                // Wraps instead of widening the row, so it never pushes the control column over.
                if (GUILayout.Button(note, _skin.DangerText) && _page != PageKeybinds)
                {
                    Select(PageKeybinds);
                }
            }
        }

        private static string DefaultText(Row row)
        {
            object def = row.Setting.DefaultValue;
            switch (row.Kind)
            {
                case RowKind.Shortcut:
                case RowKind.KeyCode:
                    return ShortcutText(def);
                case RowKind.Bool:
                    return def is bool b && b ? "on" : "off";
                case RowKind.Enum:
                case RowKind.List:
                    return SettingsModel.OptionText(def);
                default:
                    string s = def == null ? "nothing" : Convert.ToString(def, System.Globalization.CultureInfo.InvariantCulture);
                    return s.Length > 60 ? s.Substring(0, 60) + "..." : s.Length == 0 ? "empty" : s;
            }
        }
    }
}
