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

        private static readonly string[] FilterNames = { "All", "Changed", "Toggles", "Numbers", "Lists", "Keybinds", "Text", "Other" };

        private RowFilter _filter = RowFilter.All;
        private readonly int[] _filterCounts = new int[8];

        /// <summary>Reused for each group's rows after filtering, so filtering allocates nothing.</summary>
        private readonly List<Row> _groupRows = new List<Row>();
        private readonly HashSet<Row> _shownInConflicts = new HashSet<Row>();
        private readonly Dictionary<List<Row>, GUIContent> _conflictLabels = new Dictionary<List<Row>, GUIContent>();

        private void DrawContent()
        {
            ApplyMeasuredBlocks();
            if (!_query.IsEmpty)
            {
                DrawSearchResults();
                return;
            }
            switch (_page)
            {
                case PageChanged:
                    DrawChanged();
                    return;
                case PageKeybinds:
                    DrawKeybinds();
                    return;
            }
            if (_page != null && _model.PagesByGuid.TryGetValue(_page, out Page page))
            {
                DrawPage(page);
                return;
            }
            _page = PageChanged;
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
        private void DrawFilterBar(List<Row> rows)
        {
            Array.Clear(_filterCounts, 0, _filterCounts.Length);
            int hiddenCount = 0;
            foreach (Row row in rows)
            {
                // Advanced rows are always listed in search results, so only hidden ones count there.
                if (row.IsHidden || row.IsAdvanced && _query.IsEmpty)
                {
                    hiddenCount++;
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

            GUILayout.Space(U(10));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Filters.Length; i++)
            {
                RowFilter f = Filters[i];
                if (_filterCounts[i] == 0 && f != RowFilter.All && f != _filter)
                {
                    continue;
                }
                bool on = _filter == f;
                if (GUILayout.Button(CountedLabel(FilterNames[i] + "  ", _filterCounts[i], 10 + i), on ? _skin.ChipOn : _skin.Chip, GUILayout.ExpandWidth(false)))
                {
                    _filter = on && f != RowFilter.All ? RowFilter.All : f;
                }
            }
            GUILayout.FlexibleSpace();
            // Offered only where it would change something, with how much: most mods hide or
            // mark advanced nothing at all.
            if (hiddenCount > 0)
            {
                bool on = ShowHidden;
                GUIContent label = on
                    ? CountedLabel("Hide hidden  ", hiddenCount, 20)
                    : CountedLabel("Show hidden  ", hiddenCount, 21);
                if (GUILayout.Button(label, on ? _skin.ChipOn : _skin.Chip, GUILayout.ExpandWidth(false)))
                {
                    ToggleHidden();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(U(6));
        }

        // ------------------------------------------------------------------ one mod

        private void DrawPage(Page page)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Header(page.Title, page.Subtitle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            bool favorite = _favorites.Contains(page.Guid);
            if (GUILayout.Button(favorite ? "Favorite" : "Add to favorites", favorite ? _skin.ChipOn : _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                ToggleFavorite(page);
            }
            if (page.Website != null && GUILayout.Button(Cached("Website"), _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                Application.OpenURL(page.Website);
            }
            if (GUILayout.Button(Cached("Copy"), _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                CopyRows(page.Rows, page.DisplayName + " " + page.Version);
            }
            if (GUILayout.Button(Cached("Paste"), _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                Paste(page);
            }
            GUI.enabled = page.ChangedCount > 0;
            if (Confirm("reset:" + page.Guid, "Reset mod", "Reset " + page.ChangedCount + "?", _skin.GhostDanger, GUILayout.ExpandWidth(false)))
            {
                ResetRows(page.Rows, "reset " + page.DisplayName);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawFilterBar(page.Rows);

            BeginList(page.Guid);
            int drawn = 0;
            bool sectionNames = page.Sections.Count > 1 || page.Sections.Count == 1 && page.Sections[0].Name.Length > 0;
            foreach (Section section in page.Sections)
            {
                drawn += DrawGroup(section, sectionNames && section.Name.Length > 0 ? section.Label : null, _skin.SectionHeader, null, section.Rows, false);
            }
            if (drawn == 0)
            {
                EmptyNote(page.Rows.Count == 0 ? "This mod has no settings to change. It is listed so you can see everything that is loaded."
                    : _filter != RowFilter.All ? "Nothing here matches the filter."
                    : "This mod's author hides all of its settings or marks them advanced. Click \"Show hidden\" above to see them.");
            }
            EndList(page.Guid);
        }

        // ------------------------------------------------------------------ search

        private void DrawSearchResults()
        {
            Header(Cached("Search"), CountedLabel("Settings matching your search:  ", _searchResults.Count, 30));
            DrawFilterBar(_searchResults);
            BeginList("@search");
            int drawn = DrawByPage(_searchResults, null);
            if (drawn == 0)
            {
                EmptyNote(_searchResults.Count == 0
                    ? "Nothing matches. Every word has to appear in a mod's name, a setting's name, its section or its description."
                    : "Nothing here matches the filter.");
            }
            EndList("@search");
        }

        // ------------------------------------------------------------------ changed

        private void DrawChanged()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            Header(Cached("Changed settings"), Cached("Every setting that is not at its default, across all mods."));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Paste", "Apply settings copied from this menu, for every mod they cover"), _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                Paste(null);
            }
            GUI.enabled = _model.ChangedCount > 0;
            if (GUILayout.Button(Cached("Copy"), _skin.Ghost, GUILayout.ExpandWidth(false)))
            {
                CopyRows(_model.ChangedRows, "changed settings");
            }
            if (Confirm("reset:@all", "Reset all", "Reset all " + _model.ChangedCount + "?", _skin.GhostDanger, GUILayout.ExpandWidth(false)))
            {
                ResetRows(new List<Row>(_model.ChangedRows), "reset every mod");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawFilterBar(_model.ChangedRows);
            BeginList(PageChanged);
            int drawn = DrawByPage(_model.ChangedRows, null);
            if (drawn == 0)
            {
                EmptyNote(_model.ChangedCount == 0 ? "Every setting is at its default." : "Nothing here matches the filter.");
            }
            EndList(PageChanged);
        }

        // ------------------------------------------------------------------ keybinds

        private void DrawKeybinds()
        {
            int conflicts = _model.ConflictGroups.Count;
            Header(Cached("Keybinds"), Cached(conflicts == 0
                ? "Every key bound by a mod. No two settings share a key."
                : "Every key bound by a mod. Keys that more than one setting uses come first; every mod bound to one reacts to it."));
            GUILayout.Space(U(6));

            BeginList(PageKeybinds);
            _shownInConflicts.Clear();
            foreach (List<Row> group in _model.ConflictGroups)
            {
                if (!_conflictLabels.TryGetValue(group, out GUIContent title))
                {
                    title = new GUIContent(ShortcutText(group[0].Setting.Get()) + "   used by " + group.Count + " settings");
                    _conflictLabels[group] = title;
                }
                DrawGroup(group, title, _skin.GroupHeaderDanger, null, group, true);
                foreach (Row row in group)
                {
                    _shownInConflicts.Add(row);
                }
            }
            int drawn = DrawByPage(_model.KeybindRows, _shownInConflicts);
            if (_model.KeybindRows.Count == 0)
            {
                EmptyNote("No mod has a keybind setting.");
            }
            else if (drawn == 0 && conflicts == 0)
            {
                EmptyNote("Nothing here matches the filter.");
            }
            EndList(PageKeybinds);
        }

        // ------------------------------------------------------------------ lists of rows

        /// <summary>Rows from many mods, grouped under each mod's name, in the order given.</summary>
        private int DrawByPage(List<Row> rows, HashSet<Row> skip)
        {
            int drawn = 0;
            int start = 0;
            while (start < rows.Count)
            {
                Page page = rows[start].Page;
                int end = start;
                while (end < rows.Count && rows[end].Page == page)
                {
                    end++;
                }
                drawn += DrawGroup(page, page.Title, _skin.GroupHeader, page, rows, false, start, end, skip);
                start = end;
            }
            return drawn;
        }

        /// <summary>
        /// One heading and a card of rows under it. Rows the filter hides are dropped first, so
        /// the card's rounded top and bottom land on the rows actually shown, and an empty group
        /// draws nothing. Returns the number of rows drawn.
        /// </summary>
        private int DrawGroup(object key, GUIContent header, GUIStyle headerStyle, Page link, List<Row> rows, bool showPageName,
            int start = 0, int end = -1, HashSet<Row> skip = null)
        {
            if (end < 0)
            {
                end = rows.Count;
            }
            _groupRows.Clear();
            for (int i = start; i < end; i++)
            {
                Row row = rows[i];
                if (Passes(row) && (skip == null || !skip.Contains(row)))
                {
                    _groupRows.Add(row);
                }
            }
            if (_groupRows.Count == 0)
            {
                return 0;
            }

            if (header != null && BlockVisible(key))
            {
                if (link != null)
                {
                    if (GUILayout.Button(header, headerStyle, GUILayout.ExpandWidth(true)))
                    {
                        Select(link.Guid);
                    }
                }
                else
                {
                    GUILayout.Label(header, headerStyle, GUILayout.ExpandWidth(true));
                }
                MeasureBlock(key);
            }
            else if (header == null)
            {
                GUILayout.Space(U(8));
            }

            int count = _groupRows.Count;
            for (int i = 0; i < count; i++)
            {
                Row row = _groupRows[i];
                if (BlockVisible(row))
                {
                    DrawRow(row, showPageName, i == 0, i == count - 1);
                    MeasureBlock(row);
                }
            }
            return count;
        }

        private void EmptyNote(string text)
        {
            GUILayout.Space(U(24));
            GUILayout.Label(text, _skin.Muted);
        }

        // ------------------------------------------------------------------ virtualized list

        // Only rows inside (or within a screen of) the visible part of the list are drawn; the
        // rest become a GUILayout.Space of their measured height. IMGUI lays out every control
        // on every event, several events a frame, so on a 300-setting mod this is most of the cost.
        //
        // Heights are measured on Repaint into _pendingRects and only applied at the next
        // Layout event. Applying them mid-frame would let Layout and Repaint disagree about which
        // rows exist, which GUILayout punishes with an ArgumentException. Blocks carry no margins,
        // so a Space of the same height takes exactly the same room.

        private readonly Dictionary<object, Rect> _blockRects = new Dictionary<object, Rect>();
        private readonly Dictionary<object, Rect> _pendingRects = new Dictionary<object, Rect>();
        private float _listScrollY;
        private float _listViewHeight = 2000f;
        private float _pendingViewHeight;
        private string _listKey;
        private RowFilter _listFilter;
        private string _listQuery;
        private int _listWidth;
        private int _listFlags;
        private Vector2 _listScroll;

        private void ResetVirtualization()
        {
            _blockRects.Clear();
            _pendingRects.Clear();
            _listKey = null;
        }

        private void ApplyMeasuredBlocks()
        {
            if (Event.current.type != EventType.Layout)
            {
                return;
            }
            if (_pendingRects.Count > 0)
            {
                foreach (KeyValuePair<object, Rect> kv in _pendingRects)
                {
                    _blockRects[kv.Key] = kv.Value;
                }
                _pendingRects.Clear();
            }
            if (_pendingViewHeight > 0f)
            {
                _listViewHeight = _pendingViewHeight;
                _pendingViewHeight = 0f;
            }
        }

        private void BeginList(string key)
        {
            int width = Mathf.RoundToInt(_rowWidth);
            int flags = (_plugin.ShowDescriptions.Value ? 1 : 0) | (ShowAdvanced ? 2 : 0) | (ShowHidden ? 4 : 0);
            if (!ReferenceEquals(key, _listKey) || _filter != _listFilter || !ReferenceEquals(_query.Text, _listQuery)
                || width != _listWidth || flags != _listFlags)
            {
                // A different list, or the same one laid out differently: every cached position is wrong.
                if (Event.current.type == EventType.Layout)
                {
                    _blockRects.Clear();
                    _pendingRects.Clear();
                    _listKey = key;
                    _listFilter = _filter;
                    _listQuery = _query.Text;
                    _listWidth = width;
                    _listFlags = flags;
                }
            }

            _scroll.TryGetValue(key, out Vector2 pos);
            // Visibility is judged from the position before this event scrolls it, so the Layout
            // and the event that follows it agree.
            _listScrollY = pos.y;
            _listScroll = GUILayout.BeginScrollView(pos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            GUILayout.BeginVertical(GUILayout.Width(_rowWidth));
        }

        private void EndList(string key)
        {
            GUILayout.Space(U(32));
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                _pendingViewHeight = GUILayoutUtility.GetLastRect().height;
            }
            _scroll[key] = _listScroll;
        }

        private bool BlockVisible(object key)
        {
            if (!_blockRects.TryGetValue(key, out Rect r))
            {
                return true;
            }
            float overscan = _listViewHeight;
            if (r.yMax >= _listScrollY - overscan && r.y <= _listScrollY + _listViewHeight + overscan)
            {
                return true;
            }
            GUILayout.Space(r.height);
            return false;
        }

        private void MeasureBlock(object key)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                if (!_blockRects.TryGetValue(key, out Rect old) || old != r)
                {
                    _pendingRects[key] = r;
                }
            }
        }

        private void CopyRows(IEnumerable<Row> rows, string what)
        {
            List<SettingsText.Record> records = new List<SettingsText.Record>(SettingsClipboard.Records(rows));
            GUIUtility.systemCopyBuffer = SettingsText.Write(records, "Copied from " + what + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            SetStatus("Copied " + records.Count + Plural(records.Count, " setting", " settings") + " to the clipboard.");
        }

        private void Paste(Page onlyPage)
        {
            CommitEdit();
            string text = GUIUtility.systemCopyBuffer;
            if (!SettingsText.LooksLikeSettings(text))
            {
                SetStatus("The clipboard does not hold settings copied from this menu.", true);
                return;
            }
            SettingsClipboard.Result r = SettingsClipboard.Apply(text, _model, onlyPage == null ? "pasting settings" : "pasting into " + onlyPage.DisplayName, onlyPage);
            if (onlyPage != null && r.Applied == 0 && r.Unchanged == 0 && r.Unknown > 0)
            {
                SetStatus("The clipboard holds settings for other mods, not " + onlyPage.DisplayName + ". Paste on the Changed settings page applies them all.", true);
                return;
            }
            SetStatus(Describe(r, "Pasted: changed"));
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
        private void DrawRow(Row row, bool showPageName, bool first, bool last)
        {
            GUILayout.BeginHorizontal(CardStyle(first, last));
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint && row.LastRect.width > 0)
            {
                // Decorations from last frame's rect: the row's own rect is only known after it
                // is laid out, and in steady state the two are the same.
                Rect r = row.LastRect;
                if (!first)
                {
                    _skin.Fill(new Rect(r.x + U(16), r.y, r.width - U(32), 1), Skin.Divider);
                }
                if (r.Contains(Event.current.mousePosition))
                {
                    _skin.RowHover.Draw(new Rect(r.x + U(3), r.y + U(3), r.width - U(6), r.height - U(6)), false, false, false, false);
                }
                bool conflict = _model.Conflicts.ContainsKey(row);
                if (row.IsChanged || conflict)
                {
                    float h = Mathf.Max(U(16), r.height - U(28));
                    _skin.Fill(new Rect(r.x + U(5), r.y + (r.height - h) / 2f, U(3), h), conflict ? Skin.Danger : Skin.Accent);
                }
            }

            GUILayout.BeginVertical();
            if (showPageName)
            {
                GUILayout.Label(row.Page.Title, _skin.Small);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(_plugin.ShowDescriptions.Value ? row.NamePlain : row.Name, _skin.Label);
            if (row.IsChanged && row.CanReset)
            {
                if (row.ResetLabel == null)
                {
                    row.ResetLabel = new GUIContent("Reset", "Back to " + DefaultText(row));
                }
                if (GUILayout.Button(row.ResetLabel, _skin.LinkButton, GUILayout.ExpandWidth(false)))
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
            GUILayout.BeginVertical(GUILayout.Width(_valueWidth));
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
                row.LastRect = GUILayoutUtility.GetLastRect();
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
                    GUILayout.Label(HiddenTag, _skin.Tag, GUILayout.ExpandWidth(false));
                }
                if (advanced)
                {
                    GUILayout.Label(Cached("ADVANCED"), _skin.Tag, GUILayout.ExpandWidth(false));
                }
                if (readOnly)
                {
                    GUILayout.Label(Cached("READ ONLY"), _skin.Tag, GUILayout.ExpandWidth(false));
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
