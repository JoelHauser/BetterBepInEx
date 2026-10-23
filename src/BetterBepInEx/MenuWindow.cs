using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BetterBepInEx.Core;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>
    /// The replacement F12 window: a title bar with search, a sidebar of mods and special pages,
    /// and the selected page on the right. Plain IMGUI, like the stock menu, because mods with
    /// custom drawers hand over IMGUI callbacks, and those have to keep working; see
    /// <see cref="RowKind.Stock"/>.
    ///
    /// Split over three files: this one (frame, input, sidebar), MenuWindow.Pages.cs (what each
    /// page shows, and the row layout) and MenuWindow.Fields.cs (the value controls).
    /// </summary>
    internal sealed partial class MenuWindow
    {
        private const int BackdropId = 0x0F1200;
        private const int WindowId = 0x0F1201;
        private const int PopupId = 0x0F1202;
        private const string SearchControl = "bbeSearch";
        private const string ModFilterControl = "bbeModFilter";

        private const string PageChanged = "@changed";
        private const string PageKeybinds = "@keybinds";

        private readonly BetterBepInExPlugin _plugin;
        private readonly StockManager _stock;
        private readonly SettingsModel _model;
        private readonly Skin _skin = new Skin();

        private Rect _rect;
        private bool _rectLoaded;

        /// <summary>The screen size <see cref="_rect"/> was laid out for, to follow a resolution change.</summary>
        private Vector2 _rectScreen;
        private string _search = string.Empty;
        private SearchQuery _query = SearchQuery.Empty;
        private readonly List<Row> _searchResults = new List<Row>();
        private bool _searchDirty = true;

        /// <summary>The search <see cref="_searchResults"/> holds the matches for; null once they are stale.</summary>
        private SearchQuery _searchResultsFor;
        private string _page;
        private readonly Dictionary<string, Vector2> _scroll = new Dictionary<string, Vector2>();
        private Vector2 _sidebarScroll;
        private bool _focusSearch;
        private bool _focusWindow;

        private string _modFilter = string.Empty;
        private SearchQuery _modFilterQuery = SearchQuery.Empty;
        private bool _modsChangedOnly;

        private bool _resizing;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;

        private string _status;
        private bool _statusIsError;
        private float _statusTime;

        private object _confirmKey;
        private float _confirmTime;

        private readonly HashSet<string> _favorites = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Row> _failedRows = new HashSet<Row>();
        private readonly Dictionary<int, GUIContent> _countLabels = new Dictionary<int, GUIContent>();
        private readonly Dictionary<string, GUIContent> _cachedLabels = new Dictionary<string, GUIContent>();

        /// <summary>Width of the value column. Fixed per frame, so every row's control lines up.</summary>
        private float _valueWidth = 400f;
        private float _rowWidth = 800f;
        private Rect _lastStockRect;
        private Vector2 _lastStockColumns;

        // GUILayout options are objects, and each call's params array is another: made once here
        // rather than several times per row on every event.
        private static readonly GUILayoutOption[] Expand = { GUILayout.ExpandWidth(true) };
        private static readonly GUILayoutOption[] NoExpand = { GUILayout.ExpandWidth(false) };
        private readonly Dictionary<Vector3, GUILayoutOption[]> _options = new Dictionary<Vector3, GUILayoutOption[]>();

        private enum Opt
        {
            Width = 1,
            Size,
            NoExpandHeight,
            MinMaxWidth,
            SearchWidth,
        }

        private GUILayoutOption[] Opts(Opt kind, float a, float b = 0f)
        {
            Vector3 key = new Vector3((float)kind, a, b);
            if (_options.TryGetValue(key, out GUILayoutOption[] options))
            {
                return options;
            }
            if (_options.Count > 512)
            {
                // Resizing the window makes new widths every frame.
                _options.Clear();
            }
            switch (kind)
            {
                case Opt.Width:
                    options = new[] { GUILayout.Width(a) };
                    break;
                case Opt.Size:
                    options = new[] { GUILayout.Width(a), GUILayout.Height(b) };
                    break;
                case Opt.NoExpandHeight:
                    options = new[] { GUILayout.ExpandWidth(false), GUILayout.Height(a) };
                    break;
                case Opt.MinMaxWidth:
                    options = new[] { GUILayout.MinWidth(a), GUILayout.MaxWidth(b) };
                    break;
                default:
                    options = new[] { GUILayout.MinWidth(a), GUILayout.MaxWidth(b), GUILayout.ExpandWidth(true) };
                    break;
            }
            _options[key] = options;
            return options;
        }

        private GUILayoutOption[] W(float width) => Opts(Opt.Width, width);

        private readonly GUI.WindowFunction _drawBackdrop;
        private readonly GUI.WindowFunction _drawWindow;
        private readonly GUI.WindowFunction _drawPopup;

        /// <summary>The focused control's name, read once per event: Unity makes a new string each time it is asked.</summary>
        private string _focusedName = string.Empty;

        private int _inputResetFrame = -1;

        /// <summary>
        /// Bumped whenever what a list shows may have changed other than through the model (a new
        /// search, Show hidden, a rename), so the page lists its rows again.
        /// </summary>
        private int _listStamp;

        private static readonly GUIContent CloseLabel = new GUIContent("×", "Close (F12)");
        private static readonly GUIContent SearchHint = new GUIContent("Search every setting   Ctrl+F");
        private static readonly GUIContent ModFilterHint = new GUIContent("Filter mods");
        private static readonly GUIContent TitleLabel = new GUIContent("Mod Settings");
        private static readonly GUIContent UndoLabel = new GUIContent("Undo");
        private static readonly GUIContent RedoLabel = new GUIContent("Redo");

        public MenuWindow(BetterBepInExPlugin plugin, StockManager stock, SettingsModel model)
        {
            _plugin = plugin;
            _stock = stock;
            _model = model;
            _drawBackdrop = DrawBackdrop;
            _drawWindow = DrawWindow;
            _drawPopup = DrawPopup;
            foreach (string guid in (_plugin.Favorites.Value ?? string.Empty).Split('|'))
            {
                if (guid.Length > 0)
                {
                    _favorites.Add(guid);
                }
            }
            _page = string.IsNullOrEmpty(_plugin.LastPage.Value) ? PageChanged : _plugin.LastPage.Value;
            LoadCustomizations();
        }

        private float U(float v) => _skin.U(v);

        /// <summary>Hidden settings and mods (and advanced settings) are listed.</summary>
        private bool ShowHidden => _plugin.ShowHidden.Value;

        /// <summary>Advanced settings are listed: with Show hidden, and always in search results.</summary>
        private bool ShowAdvanced => _plugin.ShowHidden.Value || !_query.IsEmpty;

        private void ToggleHidden()
        {
            _plugin.ShowHidden.Value = !_plugin.ShowHidden.Value;
            _model.MarkCountsDirty();
            _listStamp++;
        }

        public void OnModelRebuilt()
        {
            _sortDirty = true;
            _searchDirty = true;
            _searchResultsFor = null;
            _listStamp++;
            _failedRows.Clear();
            _conflictLabels.Clear();
            ResetVirtualization();
            CancelCapture();
            ClosePopup();
            _editingRow = null;
        }

        /// <summary>
        /// True while the menu is in the background: clicked off, faded, and letting the game
        /// have the mouse and keys, as the stock menu did once its window had been moved.
        /// </summary>
        public bool Passive { get; private set; }

        /// <summary>The window's current opacity, eased towards its target so fading is not a jump.</summary>
        private float _alpha = 1f;

        private void EnterBackground()
        {
            EndSlide();
            CommitEdit();
            CancelCapture();
            ClosePopup();
            GUIUtility.keyboardControl = 0;
            GUI.UnfocusWindow();
            Passive = true;
        }

        public void OnOpen()
        {
            // "Recently opened" re-sorts here, not on each click, so the list holds still while in use.
            _sortDirty = true;
            Passive = false;
            _alpha = 1f;
            _focusSearch = true;
            _focusWindow = true;
            _rectLoaded = false;
            if (_page != null && !_page.StartsWith("@", StringComparison.Ordinal) && !_model.PagesByGuid.ContainsKey(_page))
            {
                _page = PageChanged;
            }
        }

        public void OnClose()
        {
            EndSlide();
            CommitEdit();
            CancelCapture();
            ClosePopup();
            _resizing = false;
            SaveRect();
            _plugin.LastPage.Value = _page ?? string.Empty;
            SaveRecent();
            // The skin is kept: rebuilding its textures is most of what reopening would cost.
        }

        // ------------------------------------------------------------------ OnGUI

        public void OnGUI()
        {
            float scale = _plugin.EffectiveScale();
            Font font = _plugin.GameFont();
            SkinOptions options = new SkinOptions
            {
                Theme = _plugin.Theme.Value,
                Accent = _plugin.AccentColor.Value,
                TextScale = Mathf.Clamp(_plugin.TextSize.Value, 0.7f, 1.6f),
                Compact = _plugin.CompactRows.Value,
            };
            if (_skin.GuiSkin == null || !Mathf.Approximately(_skin.Scale, scale) || _skin.Font != font || !_skin.Options.SameAs(options))
            {
                _skin.Build(scale, font, options);
                _countLabels.Clear();
                _sideItemHeight = 0f;
                ResetVirtualization();
            }
            GUI.skin = _skin.GuiSkin;

            EnsureRect();
            // Not mid-drag: a slider passing its default would add or drop its row from the
            // Changed page and the counts under the mouse. It all catches up on release.
            if (_slideRow == null)
            {
                _model.RefreshIfDirty(ShowHidden);
            }
            if (_searchDirty)
            {
                RebuildSearch();
            }

            Event e = Event.current;
            bool mouseOver = _rect.Contains(e.mousePosition);
            if (Passive && e.type == EventType.MouseDown && mouseOver)
            {
                // Clicking the faded menu brings it back. The click is not used up, so it also
                // lands on whatever was under it.
                Passive = false;
                _focusWindow = true;
            }
            if (e.type == EventType.Repaint)
            {
                float target = !Passive ? 1f
                    : mouseOver ? Mathf.Min(1f, _plugin.BackgroundOpacity.Value + 0.3f)
                    : _plugin.BackgroundOpacity.Value;
                _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 6f);
            }

            HandleResize(e);
            HandlePopupOutsideClick(e);

            if (!Passive)
            {
                // The dim and the shadow are a window of their own, sent to the back. Drawn as
                // plain GUI in this OnGUI they landed on top of the window: IMGUI paints windows
                // first. In the background there is neither, so nothing catches clicks meant for
                // the game.
                GUI.Window(BackdropId, new Rect(0, 0, Screen.width, Screen.height), _drawBackdrop, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToBack(BackdropId);
            }

            GUI.color = new Color(1f, 1f, 1f, _alpha);
            Rect moved = GUI.Window(WindowId, _rect, _drawWindow, GUIContent.none, _skin.Window);
            GUI.color = Color.white;
            if (moved.position != _rect.position)
            {
                _rect.position = moved.position;
                ClampRect();
            }

            if (_popup != null)
            {
                _popup.Rect = GUI.Window(PopupId, _popup.Rect, _drawPopup, GUIContent.none, _skin.Popup);
                GUI.BringWindowToFront(PopupId);
            }

            if (_focusWindow && !Passive)
            {
                GUI.FocusWindow(WindowId);
                _focusWindow = false;
            }

            if (!IsCapturing && !_stock.StockCapturingKey && !Passive && _inputResetFrame != Time.frameCount)
            {
                // Once a frame: it holds for the rest of the frame, and OnGUI runs several times.
                _inputResetFrame = Time.frameCount;
                // What the stock menu does, kept for anything outside the raid player the game
                // reads through Unity's axes. Never in the background: it makes every held key and
                // mouse button read as released, and the cursor is free there, so whenever it sat
                // over the faded window (most of the screen) the player could look around but not
                // move, shoot or use a key. 0.4.0 did exactly that.
                Input.ResetInputAxes();
            }
        }

        private void DrawBackdrop(int id)
        {
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (_plugin.DimBackground.Value)
                {
                    _skin.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.5f));
                }
                float spread = _skin.ShadowBorder - 4;
                _skin.ShadowStyle.Draw(new Rect(_rect.x - spread, _rect.y - spread + U(6), _rect.width + spread * 2, _rect.height + spread * 2),
                    false, false, false, false);
            }
            else if (e.type == EventType.MouseDown)
            {
                e.Use();
                if (_plugin.ClickOffToPlay.Value)
                {
                    EnterBackground();
                }
                else
                {
                    // This window would take keyboard focus from the menu; hand it straight back.
                    _focusWindow = true;
                }
            }
        }

        private void DrawWindow(int id)
        {
            _model.RecordDepth++;
            GUI.enabled = true;
            GUI.color = new Color(1f, 1f, 1f, _alpha);
            try
            {
                _focusedName = GUIUtility.keyboardControl == 0 ? string.Empty : GUI.GetNameOfFocusedControl();
                HandleKeys(Event.current);

                float titleHeight = _skin.T(34) + U(26);
                float statusHeight = _skin.T(30);
                float sidebarWidth = Mathf.Clamp(U(260), U(170), _rect.width * 0.32f);

                GUILayout.BeginArea(new Rect(0, 0, _rect.width, titleHeight), _skin.TitleBar);
                DrawTitleBar();
                GUILayout.EndArea();

                if (Event.current.type == EventType.Repaint)
                {
                    _skin.Fill(new Rect(1, titleHeight - 1, _rect.width - 2, 1), Skin.Divider);
                    _skin.Fill(new Rect(sidebarWidth, titleHeight, 1, _rect.height - titleHeight - U(10)), Skin.Divider);
                }

                GUILayout.BeginArea(new Rect(0, titleHeight, sidebarWidth, _rect.height - titleHeight - U(6)), _skin.Sidebar);
                DrawSidebar();
                GUILayout.EndArea();

                Rect content = new Rect(sidebarWidth + 1, titleHeight, _rect.width - sidebarWidth - 1, _rect.height - titleHeight - statusHeight);
                float padX = U(24);
                _rowWidth = content.width - padX - U(16) - U(12);
                _valueWidth = Mathf.Clamp(_rowWidth * 0.40f, U(240), U(460));
                UpdateStockGeometry();

                GUILayout.BeginArea(new Rect(content.x + padX, content.y + U(16), content.width - padX - U(10), content.height - U(16)));
                DrawContent();
                GUILayout.EndArea();

                GUILayout.BeginArea(new Rect(content.x, _rect.height - statusHeight, content.width, statusHeight));
                DrawStatusBar();
                GUILayout.EndArea();

                DrawGrip();

                // A mod's field drawn by the stock drawer may have opened a stock dropdown.
                if (!_stock.DrawStockDropdown())
                {
                    DrawTooltip();
                }

                GUI.DragWindow(new Rect(0, 0, _rect.width - U(220), titleHeight));
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                // GUILayout's "Getting control N's position in a group with only N controls"
                // when a list changes between Layout and Repaint, which happens on the frame a
                // setting appears or disappears. The next frame lays out cleanly; the stock menu
                // swallows the same exception.
            }
            finally
            {
                _model.RecordDepth--;
            }
        }

        private void UpdateStockGeometry()
        {
            Vector2 columns = new Vector2(_rowWidth - _valueWidth, _valueWidth);
            if (_rect != _lastStockRect || columns != _lastStockColumns)
            {
                _lastStockRect = _rect;
                _lastStockColumns = columns;
                _stock.SetGeometry(_rect, (int)columns.x, (int)columns.y);
            }
        }

        private void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown)
            {
                return;
            }
            bool ctrl = e.control || e.command;
            bool typing = GUIUtility.keyboardControl != 0;

            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _editingRow != null)
            {
                CommitEdit();
                GUIUtility.keyboardControl = 0;
                e.Use();
                return;
            }
            if (ctrl && e.keyCode == KeyCode.F)
            {
                _focusSearch = true;
                e.Use();
                return;
            }
            if (ctrl && !typing && e.keyCode == KeyCode.Z && !e.shift)
            {
                Undo();
                e.Use();
                return;
            }
            if (ctrl && !typing && (e.keyCode == KeyCode.Y || e.keyCode == KeyCode.Z && e.shift))
            {
                Redo();
                e.Use();
            }
        }

        // ------------------------------------------------------------------ title bar

        private void DrawTitleBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(TitleLabel, _skin.Title, Opts(Opt.NoExpandHeight, _skin.T(34)));
            GUILayout.Space(U(20));

            string typed = SearchBox(SearchControl, _search, SearchHint, _skin.SearchField, out bool cleared, Opts(Opt.SearchWidth, U(180), U(560)));
            if (typed != _search)
            {
                SetSearch(typed);
            }
            if (cleared)
            {
                _focusSearch = true;
            }
            if (_focusSearch)
            {
                GUI.FocusControl(SearchControl);
                _focusSearch = false;
            }

            GUILayout.FlexibleSpace();

            AdvancedToggle();
            GUILayout.Space(U(10));

            GUI.enabled = _model.History.CanUndo;
            UndoLabel.tooltip = UndoTip();
            if (GUILayout.Button(UndoLabel, _skin.Ghost, Opts(Opt.NoExpandHeight, _skin.T(34))))
            {
                Undo();
            }
            GUI.enabled = _model.History.CanRedo;
            RedoLabel.tooltip = RedoTip();
            if (GUILayout.Button(RedoLabel, _skin.Ghost, Opts(Opt.NoExpandHeight, _skin.T(34))))
            {
                Redo();
            }
            GUI.enabled = true;
            GUILayout.Space(U(6));
            if (GUILayout.Button(CloseLabel, _skin.IconButton))
            {
                _stock.DisplayingWindow = false;
            }
            GUILayout.EndHorizontal();
        }

        private static readonly GUIContent ClearLabel = new GUIContent("×", "Clear");
        private static readonly GUIContent AdvancedLabel = new GUIContent("Show advanced",
            "List the settings, and the mods, that their authors mark as advanced or hide from the menu. "
            + "They are tagged where they appear. Search always finds them.");
        private readonly GUIContent _fieldContent = new GUIContent();

        /// <summary>
        /// A text field with a search glass and a placeholder drawn inside it and, once something
        /// is typed, a × at its right end that clears it. The × lives inside the box so the title
        /// bar does not shift when typing starts, as a separate Clear button made it.
        /// </summary>
        private string SearchBox(string control, string text, GUIContent hint, GUIStyle style, out bool cleared, params GUILayoutOption[] options)
        {
            _fieldContent.text = text;
            Rect field = GUILayoutUtility.GetRect(_fieldContent, style, options);
            Rect clear = new Rect(field.xMax - field.height, field.y, field.height, field.height);
            // Before the field, so the click reaches it first; drawn after it, so it shows on top.
            // A zero rect while empty: the button is always called, so the controls never change.
            cleared = GUI.Button(text.Length > 0 ? clear : Rect.zero, GUIContent.none, GUIStyle.none) && text.Length > 0;
            GUI.SetNextControlName(control);
            string typed = GUI.TextField(field, text, style);
            if (Event.current.type == EventType.Repaint)
            {
                float icon = Mathf.Min(U(16), field.height - U(8));
                _skin.Fill(new Rect(field.x + style.padding.left - icon - U(8), field.y + (field.height - icon) / 2f, icon, icon), Skin.MutedColor, _skin.SearchIcon);
                if (text.Length == 0 && _focusedName != control)
                {
                    _skin.Muted.Draw(new Rect(field.x + style.padding.left - U(2), field.y, field.width - style.padding.left, field.height),
                        hint, false, false, false, false);
                }
                else if (text.Length > 0)
                {
                    _skin.ClearGlyph.Draw(clear, ClearLabel, clear.Contains(Event.current.mousePosition), false, false, false);
                }
            }
            return cleared ? string.Empty : typed;
        }

        /// <summary>
        /// Show advanced, in the title bar on every page: the one place to reveal what mods hide,
        /// whatever page is open. A labelled switch, so its state reads at a glance.
        /// </summary>
        private void AdvancedToggle()
        {
            GUIStyle style = _skin.HeaderToggle;
            Rect r = GUILayoutUtility.GetRect(AdvancedLabel, style, NoExpand);
            bool on = ShowHidden;
            if (GUI.Button(r, AdvancedLabel, style))
            {
                ToggleHidden();
            }
            if (Event.current.type == EventType.Repaint)
            {
                float h = U(24);
                float w = U(40);
                Rect pill = new Rect(r.xMax - U(12) - w, r.y + Mathf.Round((r.height - h) / 2f), w, h);
                DrawPill(pill, on, AdvancedLabel, r.Contains(Event.current.mousePosition));
            }
        }

        // The tooltips are rebuilt only when the next undo or redo changes, not on every event.
        private string _undoTipFor;
        private string _undoTip = "Nothing to undo";
        private string _redoTipFor;
        private string _redoTip = "Nothing to redo";

        private string UndoTip()
        {
            string label = _model.History.NextUndoLabel;
            if (!ReferenceEquals(label, _undoTipFor))
            {
                _undoTipFor = label;
                _undoTip = label == null ? "Nothing to undo" : "Undo " + label + "  (Ctrl+Z)";
            }
            return _undoTip;
        }

        private string RedoTip()
        {
            string label = _model.History.NextRedoLabel;
            if (!ReferenceEquals(label, _redoTipFor))
            {
                _redoTipFor = label;
                _redoTip = label == null ? "Nothing to redo" : "Redo " + label + "  (Ctrl+Y)";
            }
            return _redoTip;
        }

        private void SetSearch(string text)
        {
            _search = text ?? string.Empty;
            _query = new SearchQuery(_search);
            _searchDirty = true;
            ClosePopup();
        }

        private void RebuildSearch()
        {
            _searchDirty = false;
            if (_query.IsEmpty)
            {
                _searchResults.Clear();
                _searchResultsFor = null;
                return;
            }
            FindSearchResults();
            _searchSummary.text = SearchSummary();
        }

        private readonly GUIContent _searchSummary = new GUIContent();

        /// <summary>"12 settings in 4 mods": the search page's subtitle, made once per search.</summary>
        private string SearchSummary()
        {
            int mods = 0;
            Page last = null;
            foreach (Row row in _searchResults)
            {
                if (row.Page != last)
                {
                    mods++;
                    last = row.Page;
                }
            }
            return _searchResults.Count == 0 ? "No setting matches."
                : _searchResults.Count + Plural(_searchResults.Count, " setting", " settings") + " in " + mods + Plural(mods, " mod", " mods");
        }

        private void FindSearchResults()
        {
            if (_searchResultsFor != null && _query.Narrows(_searchResultsFor))
            {
                // Typing on: every match is among the last ones, so only they are checked.
                _searchResults.RemoveAll(row => !_query.Matches(row.Haystack));
                _searchResultsFor = _query;
                _listStamp++;
                return;
            }
            _searchResults.Clear();
            _searchResultsFor = _query;
            _listStamp++;
            foreach (Page page in _model.Pages)
            {
                foreach (Row row in page.Rows)
                {
                    if (_query.Matches(row.Haystack))
                    {
                        _searchResults.Add(row);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ sidebar

        /// <summary>One line of the mods list below its header: a mod, or an author's name when grouped by author.</summary>
        private struct SideEntry
        {
            public Page Page;
            public GUIContent Header;
        }

        // The mods list is built on Layout and kept for the event that follows, like the settings
        // list, and only the lines in view are drawn: every line has a fixed height, so where each
        // one falls is known without measuring its text.
        private readonly List<Page> _sideFavorites = new List<Page>();
        private readonly List<SideEntry> _sideEntries = new List<SideEntry>();
        private int _sideShown;
        private int _sideFirst;
        private int _sideEnd;
        private float _sideTopSpace;
        private float _sideBottomSpace;

        /// <summary>Where the mods list starts in the sidebar's scroll content, measured on Repaint.</summary>
        private float _sideListTop;
        private float _sideViewHeight = 2000f;
        private float _sideItemHeight;
        private float _sideHeaderHeight;

        private float SideItemStep => _sideItemHeight + _skin.SidebarItem.margin.bottom;

        private void DrawSidebar()
        {
            EventType type = Event.current.type;
            string typed = SearchBox(ModFilterControl, _modFilter, ModFilterHint, _skin.SidebarFilter, out _, Expand);
            if (typed != _modFilter)
            {
                _modFilter = typed;
                _modFilterQuery = new SearchQuery(typed);
            }
            GUILayout.Space(U(6));

            if (_sideItemHeight <= 0f)
            {
                _sideItemHeight = _skin.SidebarItem.fixedHeight > 0f ? _skin.SidebarItem.fixedHeight : _skin.SidebarItem.CalcHeight(Cached("Ag"), 200f);
                _sideHeaderHeight = _skin.SidebarHeader.CalcHeight(Cached("AG"), 200f);
            }
            List<Page> pages = SortedPages();
            if (type == EventType.Layout)
            {
                BuildSidebar(pages);
            }

            Vector2 scroll = _sidebarScroll;
            _sidebarScroll = GUILayout.BeginScrollView(_sidebarScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            if (_sidebarScroll != scroll)
            {
                _listMovedAt = Time.realtimeSinceStartup;
            }

            SidebarItem(PageChanged, "Changed settings", _model.ChangedCount, _skin.Badge);
            int conflicts = _model.ConflictGroups.Count;
            SidebarItem(PageKeybinds, "Keybinds", conflicts > 0 ? conflicts : 0, _skin.BadgeDanger);

            if (_sideFavorites.Count > 0)
            {
                GUILayout.Label(Cached("FAVORITES"), _skin.SidebarHeader);
                foreach (Page page in _sideFavorites)
                {
                    SidebarPage(page);
                }
            }

            GUILayout.BeginHorizontal();
            int listed = ShowHidden ? _model.Pages.Count : _model.Pages.Count - _hiddenListCount;
            GUILayout.Label(CountedLabel("MODS  ", listed, 1), _skin.SidebarHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Cached(_modsChangedOnly ? "Changed only" : "All"), _skin.SidebarToggle, NoExpand))
            {
                _modsChangedOnly = !_modsChangedOnly;
            }
            GUILayout.EndHorizontal();
            if (_hiddenListCount > 0)
            {
                // Mods with no settings, mods whose author hid all of them (the stock menu never
                // listed either), and mods the player hid from the list.
                GUIContent toggle = ShowHidden
                    ? CountedLabel("Hide hidden mods  ", _hiddenListCount, 5)
                    : CountedLabel("Show hidden mods  ", _hiddenListCount, 6);
                if (GUILayout.Button(toggle, _skin.SidebarHiddenToggle, Expand))
                {
                    ToggleHidden();
                }
            }

            // A zero-height space: its top is where the list starts, past the margin above it.
            GUILayout.Space(0f);
            if (type == EventType.Repaint)
            {
                _sideListTop = GUILayoutUtility.GetLastRect().y;
            }
            if (type == EventType.Layout)
            {
                ArrangeSidebar(scroll.y);
            }
            GUILayout.Space(_sideTopSpace);
            for (int i = _sideFirst; i < _sideEnd; i++)
            {
                SideEntry entry = _sideEntries[i];
                if (entry.Page != null)
                {
                    SidebarPage(entry.Page);
                }
                else
                {
                    Rect r = GUILayoutUtility.GetRect(1f, _sideHeaderHeight, GUIStyle.none, Expand);
                    if (type == EventType.Repaint)
                    {
                        _skin.SidebarHeader.Draw(r, entry.Header, false, false, false, false);
                    }
                }
            }
            GUILayout.Space(_sideBottomSpace);
            if (_sideShown == 0)
            {
                GUILayout.Label(Cached(_modsChangedOnly ? "No mod has a changed setting." : "No mod matches."), _skin.Small);
            }

            GUILayout.EndScrollView();
            if (type == EventType.Repaint)
            {
                _sideViewHeight = GUILayoutUtility.GetLastRect().height;
            }
        }

        private void BuildSidebar(List<Page> pages)
        {
            SearchQuery filter = _modFilterQuery;
            _sideFavorites.Clear();
            _sideEntries.Clear();
            _sideShown = 0;
            bool byAuthor = _sortedBy == ModSort.Author;
            GUIContent lastAuthor = null;

            // This menu's own settings head the list, in the accent, whatever the sort: the
            // first place anyone looks for how to change the menu.
            if (_model.PagesByGuid.TryGetValue(BetterBepInExPlugin.PluginGuid, out Page own) && SidebarShows(own, filter))
            {
                _sideEntries.Add(new SideEntry { Page = own });
                _sideShown++;
                if (_favorites.Contains(own.Guid))
                {
                    _sideFavorites.Add(own);
                }
            }
            foreach (Page page in pages)
            {
                if (page == own || !SidebarShows(page, filter))
                {
                    continue;
                }
                if (_favorites.Contains(page.Guid))
                {
                    _sideFavorites.Add(page);
                }
                if (byAuthor)
                {
                    GUIContent author = Cached(string.IsNullOrEmpty(page.Author) ? "OTHER" : page.AuthorUpper);
                    if (author != lastAuthor)
                    {
                        _sideEntries.Add(new SideEntry { Header = author });
                        lastAuthor = author;
                    }
                }
                _sideEntries.Add(new SideEntry { Page = page });
                _sideShown++;
            }
        }

        /// <summary>Picks the lines of the mods list in view. Headers and mods have fixed heights and no margins.</summary>
        private void ArrangeSidebar(float scroll)
        {
            float step = SideItemStep;
            float top = scroll - _sideListTop - step;
            float bottom = scroll - _sideListTop + _sideViewHeight + step;
            int count = _sideEntries.Count;
            float y = 0f;
            float drawnEnd = -1f;
            _sideFirst = count;
            _sideEnd = count;
            for (int i = 0; i < count; i++)
            {
                float h = _sideEntries[i].Page != null ? step : _sideHeaderHeight;
                if (_sideFirst == count && y + h > top)
                {
                    _sideFirst = i;
                    _sideTopSpace = y;
                }
                if (_sideFirst < count && drawnEnd < 0f && y >= bottom)
                {
                    _sideEnd = i;
                    drawnEnd = y;
                }
                y += h;
            }
            if (_sideFirst == count)
            {
                _sideTopSpace = y;
            }
            _sideBottomSpace = drawnEnd < 0f ? 0f : y - drawnEnd;
        }

        private bool SidebarShows(Page page, SearchQuery filter)
        {
            if (_modsChangedOnly && page.ChangedCount == 0 || ListHidden(page) && !ShowHidden)
            {
                return false;
            }
            return filter.IsEmpty || filter.Matches(page.SidebarHaystack ?? (page.SidebarHaystack =
                SearchQuery.Haystack(page.DisplayName, page.OriginalName, page.Guid, page.Author)));
        }

        private void SidebarPage(Page page)
        {
            bool selected = _query.IsEmpty && _page == page.Guid;
            if (SidebarButton(page.SidebarLabel, selected, page.ChangedCount, _skin.BadgeMuted, ListHidden(page),
                    page.Guid == BetterBepInExPlugin.PluginGuid))
            {
                Select(page.Guid);
            }
        }

        private void SidebarItem(string key, string label, int count, GUIStyle badge)
        {
            bool selected = _query.IsEmpty && _page == key;
            if (SidebarButton(Cached(label), selected, count, badge, false))
            {
                Select(key);
            }
        }

        /// <summary>
        /// One line of the sidebar. It takes its fixed height plus the gap below it, with no
        /// margins, so a run of lines out of view is exactly a Space of their count times that.
        /// </summary>
        private bool SidebarButton(GUIContent label, bool selected, int count, GUIStyle badgeStyle, bool dimmed, bool own = false)
        {
            Rect slot = GUILayoutUtility.GetRect(1f, SideItemStep, GUIStyle.none, Expand);
            Rect r = new Rect(slot.x, slot.y, slot.width, _sideItemHeight);
            if (selected && Event.current.type == EventType.Repaint)
            {
                _skin.SidebarSelected.Draw(r, false, false, false, false);
            }
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);

            if (Event.current.type != EventType.Repaint)
            {
                return clicked;
            }
            bool hover = !ListMoving && r.Contains(Event.current.mousePosition);
            if (hover && !selected)
            {
                // Drawn here rather than by the button, so it waits while the list scrolls.
                _skin.SidebarItem.Draw(r, true, false, false, false);
            }
            if (selected)
            {
                float barHeight = r.height - U(14);
                _skin.Fill(new Rect(r.x + U(3), r.y + (r.height - barHeight) / 2f, U(3), barHeight), Skin.Accent);
            }
            float badgeWidth = 0f;
            if (count > 0 && badgeStyle != null)
            {
                GUIContent badge = CountLabel(count);
                Vector2 size = badgeStyle.CalcSize(badge);
                badgeWidth = size.x + U(8);
                badgeStyle.Draw(new Rect(r.xMax - size.x - U(8), r.y + (r.height - size.y) / 2f, size.x, size.y), badge, false, false, false, false);
            }
            Rect text = new Rect(r.x, r.y, r.width - badgeWidth, r.height);
            GUIStyle textStyle = own ? _skin.SidebarTextOwn : selected ? _skin.SidebarTextActive : dimmed ? _skin.SidebarTextHidden : _skin.SidebarText;
            textStyle.Draw(text, label, hover, false, false, false);
            return clicked;
        }

        private void Select(string page)
        {
            CommitEdit();
            ClosePopup();
            if (!_query.IsEmpty)
            {
                SetSearch(string.Empty);
            }
            if (_page != page)
            {
                _filter = RowFilter.All;
            }
            _page = page;
            if (!page.StartsWith("@", StringComparison.Ordinal))
            {
                NoteOpened(page);
            }
        }

        private GUIContent CountLabel(int count)
        {
            if (!_countLabels.TryGetValue(count, out GUIContent c))
            {
                c = new GUIContent(count.ToString(CultureInfo.InvariantCulture));
                _countLabels[count] = c;
            }
            return c;
        }

        /// <summary>"prefix + count + suffix", cached per (slot, count) so labels cost no allocation per frame.</summary>
        private GUIContent CountedLabel(string prefix, int count, int slot, string suffix = null)
        {
            int key = -(slot * 1000003 + count + 1);
            if (!_countLabels.TryGetValue(key, out GUIContent c))
            {
                c = new GUIContent(prefix + count.ToString(CultureInfo.InvariantCulture) + suffix);
                _countLabels[key] = c;
            }
            return c;
        }

        private GUIContent Cached(string text)
        {
            if (!_cachedLabels.TryGetValue(text, out GUIContent c))
            {
                c = new GUIContent(text);
                _cachedLabels[text] = c;
            }
            return c;
        }

        private void ToggleFavorite(Page page)
        {
            if (!_favorites.Remove(page.Guid))
            {
                _favorites.Add(page.Guid);
            }
            _plugin.Favorites.Value = string.Join("|", new List<string>(_favorites).ToArray());
        }

        // ------------------------------------------------------------------ status, grip, tooltip

        private void DrawStatusBar()
        {
            GUILayout.BeginHorizontal();
            if (_status != null && Time.realtimeSinceStartup - _statusTime < 8f)
            {
                Color old = _skin.Status.normal.textColor;
                _skin.Status.normal.textColor = _statusIsError ? Skin.Danger : Skin.Accent;
                GUILayout.Label(_status, _skin.Status);
                _skin.Status.normal.textColor = old;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(CountedLabel("mods  ", _model.Pages.Count, 2), _skin.Status, NoExpand);
            GUILayout.Label(CountedLabel("settings  ", _model.AllRows.Count, 3), _skin.Status, NoExpand);
            GUILayout.Label(CountedLabel("changed  ", _model.ChangedCount, 4), _skin.Status, NoExpand);
            GUILayout.Space(U(18));
            GUILayout.EndHorizontal();
        }

        private void SetStatus(string text, bool error = false)
        {
            _status = text;
            _statusIsError = error;
            _statusTime = Time.realtimeSinceStartup;
        }

        private Rect GripRect => new Rect(_rect.xMax - U(18), _rect.yMax - U(18), U(18), U(18));

        private void DrawGrip()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            // Six dots in a triangle against the bottom-right corner, in window coordinates.
            float size = U(2);
            float step = U(4);
            float right = _rect.width - U(7);
            float bottom = _rect.height - U(7);
            for (int col = 0; col < 3; col++)
            {
                for (int row = 0; row < 3 - col; row++)
                {
                    _skin.Fill(new Rect(right - size - col * step, bottom - size - row * step, size, size), Skin.MutedColor);
                }
            }
        }

        /// <summary>How long the cursor rests on something before its tooltip shows.</summary>
        private const float TooltipDelay = 0.3f;

        private string _tipPending;
        private float _tipSince;

        private void DrawTooltip()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            // Shown once the cursor rests, and never while the list scrolls under it: otherwise
            // every setting name it crosses flashes its description up.
            string tip = GUI.tooltip;
            float now = Time.realtimeSinceStartup;
            if (string.IsNullOrEmpty(tip) || ListMoving)
            {
                _tipPending = null;
                return;
            }
            if (tip != _tipPending)
            {
                _tipPending = tip;
                _tipSince = now;
            }
            if (now - _tipSince < TooltipDelay)
            {
                return;
            }
            GUIContent content = Cached(tip);
            float width = Mathf.Min(U(420), _rect.width - U(20));
            float textWidth = _skin.Tooltip.CalcSize(content).x;
            width = Mathf.Min(width, textWidth);
            float height = _skin.Tooltip.CalcHeight(content, width);
            Vector2 m = Event.current.mousePosition;
            float x = Mathf.Min(m.x + U(14), _rect.width - width - U(6));
            float y = m.y + U(22) + height > _rect.height ? m.y - height - U(8) : m.y + U(22);
            _skin.Tooltip.Draw(new Rect(x, y, width, height), content, false, false, false, false);
        }

        // ------------------------------------------------------------------ window geometry

        /// <summary>
        /// The window's place and size are kept as fractions of the screen, so a window sized at
        /// 3440x1440 comes back the same shape at 1920x1080 or 4K, and a resolution change while
        /// the menu is open moves it along with the screen.
        /// </summary>
        private void EnsureRect()
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            if (!_rectLoaded)
            {
                _rectLoaded = true;
                if (TryParseRect(_plugin.WindowRect.Value, out Rect fraction))
                {
                    _rect = new Rect(fraction.x * screen.x, fraction.y * screen.y, fraction.width * screen.x, fraction.height * screen.y);
                }
                else
                {
                    _rect = DefaultRect();
                }
                _rectScreen = screen;
            }
            else if (screen != _rectScreen && _rectScreen.x > 0f && _rectScreen.y > 0f)
            {
                float sx = screen.x / _rectScreen.x;
                float sy = screen.y / _rectScreen.y;
                _rect = new Rect(_rect.x * sx, _rect.y * sy, _rect.width * sx, _rect.height * sy);
                _rectScreen = screen;
                ResetVirtualization();
            }
            ClampRect();
        }

        /// <summary>Centred, 84% of the screen's height, and as wide as the layout uses at this scale.</summary>
        private Rect DefaultRect()
        {
            float w = Mathf.Min(Screen.width * 0.9f, Mathf.Max(Screen.width * 0.55f, U(1320)));
            float h = Screen.height * 0.84f;
            return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        }

        private void ClampRect()
        {
            float minW = Mathf.Min(Screen.width, U(760));
            float minH = Mathf.Min(Screen.height, U(440));
            _rect.width = Mathf.Clamp(_rect.width, minW, Screen.width);
            _rect.height = Mathf.Clamp(_rect.height, minH, Screen.height);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Screen.width - _rect.width);
            _rect.y = Mathf.Clamp(_rect.y, 0f, Screen.height - _rect.height);
        }

        private void SaveRect()
        {
            if (_rectLoaded && Screen.width > 0 && Screen.height > 0)
            {
                _plugin.WindowRect.Value = string.Format(CultureInfo.InvariantCulture, "{0:0.####},{1:0.####},{2:0.####},{3:0.####}",
                    _rect.x / Screen.width, _rect.y / Screen.height, _rect.width / Screen.width, _rect.height / Screen.height);
            }
        }

        private static bool TryParseRect(string text, out Rect rect)
        {
            rect = default;
            string[] parts = (text ?? string.Empty).Split(',');
            if (parts.Length != 4)
            {
                return false;
            }
            float[] v = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                {
                    return false;
                }
            }
            rect = new Rect(v[0], v[1], v[2], v[3]);
            // Fractions of the screen; anything else is not a rect this version saved.
            return v[2] > 0.05f && v[3] > 0.05f && v[2] <= 1f && v[3] <= 1f && v[0] >= 0f && v[1] >= 0f && v[0] < 1f && v[1] < 1f;
        }

        public void ResetWindow()
        {
            _plugin.WindowRect.Value = string.Empty;
            _rectLoaded = false;
        }

        /// <summary>
        /// Resizing is handled here, outside the window function, so the drag keeps working when the
        /// cursor leaves the window, which it does whenever the window is being made smaller.
        /// </summary>
        private void HandleResize(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && _popup == null && GripRect.Contains(e.mousePosition):
                    _resizing = true;
                    _resizeStartMouse = e.mousePosition;
                    _resizeStartSize = _rect.size;
                    e.Use();
                    break;
                case EventType.MouseDrag when _resizing:
                    Vector2 delta = e.mousePosition - _resizeStartMouse;
                    _rect.width = _resizeStartSize.x + delta.x;
                    _rect.height = _resizeStartSize.y + delta.y;
                    ClampRect();
                    e.Use();
                    break;
                case EventType.MouseUp when _resizing:
                    _resizing = false;
                    SaveRect();
                    e.Use();
                    break;
            }
        }

        // ------------------------------------------------------------------ undo

        private void Undo()
        {
            CommitEdit();
            if (_model.Undo(out string label))
            {
                SetStatus("Undid " + label + ".");
            }
        }

        private void Redo()
        {
            CommitEdit();
            if (_model.Redo(out string label))
            {
                SetStatus("Redid " + label + ".");
            }
        }

        /// <summary>
        /// Two-click confirmation for anything that changes many settings at once. The first click
        /// arms the button for four seconds and relabels it "<paramref name="armedPrefix"/>N?"; the
        /// second does it. The armed label is only made while armed.
        /// </summary>
        private bool Confirm(object key, string label, string armedPrefix, int count, GUIStyle style, params GUILayoutOption[] options)
        {
            bool armed = Equals(_confirmKey, key) && Time.realtimeSinceStartup - _confirmTime < 4f;
            GUIContent content = armed ? Cached(armedPrefix + count + "?") : Cached(label);
            if (!GUILayout.Button(content, armed ? _skin.ButtonDanger : style, options))
            {
                return false;
            }
            if (armed)
            {
                _confirmKey = null;
                return true;
            }
            _confirmKey = key;
            _confirmTime = Time.realtimeSinceStartup;
            return false;
        }

        // ------------------------------------------------------------------ helpers

        internal static string ShortcutText(object value)
        {
            if (value is KeyboardShortcut shortcut)
            {
                if (shortcut.MainKey == KeyCode.None)
                {
                    return "Not bound";
                }
                string text = string.Empty;
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    text += KeyName(modifier) + " + ";
                }
                return text + KeyName(shortcut.MainKey);
            }
            if (value is KeyCode key)
            {
                return key == KeyCode.None ? "Not bound" : KeyName(key);
            }
            return value == null ? "Not bound" : value.ToString();
        }

        /// <summary>A key's name as printed on a keyboard rather than as Unity spells it.</summary>
        internal static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.AltGr:
                    return "AltGr";
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                    return "Win";
                case KeyCode.Return:
                    return "Enter";
                case KeyCode.Escape:
                    return "Esc";
                case KeyCode.BackQuote:
                    return "`";
                case KeyCode.Mouse0:
                    return "Left mouse";
                case KeyCode.Mouse1:
                    return "Right mouse";
                case KeyCode.Mouse2:
                    return "Middle mouse";
                case KeyCode.UpArrow:
                    return "Up";
                case KeyCode.DownArrow:
                    return "Down";
                case KeyCode.LeftArrow:
                    return "Left";
                case KeyCode.RightArrow:
                    return "Right";
            }
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return ((int)(key - KeyCode.Alpha0)).ToString(CultureInfo.InvariantCulture);
            }
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return "Num " + (int)(key - KeyCode.Keypad0);
            }
            if (key >= KeyCode.Mouse3 && key <= KeyCode.Mouse6)
            {
                return "Mouse " + (int)(key - KeyCode.Mouse0 + 1);
            }
            return DisplayNames.SplitWords(key.ToString());
        }

        private static string Plural(int n, string one, string many) => n == 1 ? one : many;
    }
}
