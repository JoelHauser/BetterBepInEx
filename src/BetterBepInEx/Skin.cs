using System.Collections.Generic;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>The menu's colour themes. The descriptions are what the settings dropdown shows.</summary>
    public enum MenuTheme
    {
        [System.ComponentModel.Description("Dark")]
        Dark,

        [System.ComponentModel.Description("Black (OLED)")]
        Black,

        [System.ComponentModel.Description("Grey")]
        Grey,

        [System.ComponentModel.Description("High contrast")]
        HighContrast,

        [System.ComponentModel.Description("Tan")]
        Tan,
    }

    /// <summary>Everything the skin is built from besides scale and font; a change rebuilds it.</summary>
    internal struct SkinOptions
    {
        public MenuTheme Theme;
        public Color Accent;
        public float TextScale;
        public bool Compact;

        public bool SameAs(SkinOptions other)
        {
            return Theme == other.Theme && Accent == other.Accent && Mathf.Approximately(TextScale, other.TextScale)
                   && Compact == other.Compact;
        }
    }

    /// <summary>
    /// The menu's IMGUI skin. Built in code: rounded-rect, circle, shadow and icon textures drawn
    /// pixel by pixel, rounded ones nine-sliced, on a dark palette with EFT's tan as the accent.
    ///
    /// Scaling is done by building the skin at the target size (font sizes, padding, heights)
    /// rather than with GUI.matrix. A matrix scale stretches text rasterized at the small size,
    /// which is why the stock menu with a scale add-on looks soft at 1440p.
    ///
    /// The skin is a copy of the default one with every style restyled, so the stock drawer's
    /// fields (drawn inside this menu for mods with custom drawers) pick it up too.
    /// </summary>
    internal sealed class Skin
    {
        // The palette. Static so drawing code anywhere can use it; set by ApplyPalette at the
        // start of every Build, from the theme and accent colour chosen in the settings.
        public static Color WindowBg, WindowOutline, CardBg, CardBorder, Divider, Hover, Selected, HoverOverlay;
        public static Color InputBg, InputBorder, InputHoverBorder, FieldHoverBg, SearchBg;
        public static Color ButtonBg, ButtonHover, ButtonActive;
        public static Color TextColor, SoftText, MutedColor;
        public static Color Accent, AccentText, AccentSoft, Danger, DangerBg;
        public static Color TrackOffColor, KeycapBg, KeycapEdge, KeycapLip, TagBg, BadgeMutedBg, ScrollThumb;
        public static Color PopupBg, PopupBorder, TooltipBg;

        public SkinOptions Options { get; private set; }

        public float Scale { get; private set; }
        public Font Font => _font;
        public GUISkin GuiSkin { get; private set; }

        public GUIStyle Window, Sidebar, Card, TitleBar;
        public GUIStyle Title, PageTitle, PageSubtitle, SectionHeader, GroupHeader, GroupHeaderDanger;
        public GUIStyle Label, Muted, Small, Description, DangerText, Badge, BadgeDanger, BadgeMuted, Tag;
        public GUIStyle SidebarItem, SidebarText, SidebarTextActive, SidebarTextHidden, SidebarTextOwn, SidebarHeader, SidebarSelected, SidebarToggle, SidebarFilter, SidebarHiddenToggle;
        public GUIStyle CardTop, CardMid, CardBottom, CardSingle, RowHover, ShadowStyle, KeyHint, KeyHintActive;
        public GUIStyle Button, ButtonAccent, ButtonDanger, Ghost, GhostOn, GhostDanger, IconButton, LinkButton, Dropdown, HeaderToggle, ClearGlyph;
        public GUIStyle TextField, SearchField, NumberField, KeyField, KeyFieldActive, Keycap, KeyPlus;
        public GUIStyle Chip, ChipOn, FilterChip, FilterChipOn, TrackOn, TrackOff;
        public GUIStyle RowStyle, Popup, PopupItem, PopupItemActive, Status, Tooltip;
        public GUIStyle Slider, SliderThumb, SliderTrack, SliderFill;

        public Texture2D WhiteTex, KnobTex, SearchIcon, ShadowTex;

        /// <summary>Nine-slice border of <see cref="ShadowTex"/>, in texture pixels.</summary>
        public int ShadowBorder { get; private set; }

        private readonly List<Object> _owned = new List<Object>();
        private Font _font;

        public float U(float v) => Mathf.Round(v * Scale);

        /// <summary>
        /// Sizes that hold a line of text (field heights, list rows) grow with the text size as
        /// well as the menu scale, or bigger text would be clipped.
        /// </summary>
        public float T(float v) => Mathf.Round(v * Scale * Options.TextScale);

        public void Build(float scale, Font font, SkinOptions options)
        {
            Destroy();
            Scale = scale;
            _font = font;
            Options = options;
            ApplyPalette(options.Theme, options.Accent);
            bool compact = options.Compact;

            GuiSkin = Object.Instantiate(GUI.skin);
            // EFT unloads unused assets on scene changes; this keeps the skin through a raid load.
            GuiSkin.hideFlags = HideFlags.HideAndDontSave;
            _owned.Add(GuiSkin);
            if (_font != null)
            {
                GuiSkin.font = _font;
            }

            WhiteTex = Solid(Color.white);
            KnobTex = Circle(Color.white, 16);
            SearchIcon = Magnifier(32);
            ShadowBorder = R(30);
            ShadowTex = Shadow(ShadowBorder);

            Texture2D hover = Rounded(Hover, 6, null);
            Texture2D selected = Rounded(Selected, 6, null);

            // ---------------------------------------------------------------- surfaces
            Window = Make(null, Rounded(WindowBg, 10, WindowOutline), 10);
            Window.padding = Offset(0);
            Sidebar = new GUIStyle { padding = Offset(U(12), U(8), U(14), U(10)) };
            ShadowStyle = new GUIStyle { border = new RectOffset(ShadowBorder, ShadowBorder, ShadowBorder, ShadowBorder) };
            ShadowStyle.normal.background = ShadowTex;

            // A section's settings sit in one card. Each row draws its own piece of it (top,
            // middle, bottom, or a whole card for a single row), so a row can be skipped when it
            // is scrolled out of view without breaking the card.
            CardTop = CardPiece(true, false);
            CardMid = CardPiece(false, false);
            CardBottom = CardPiece(false, true);
            CardSingle = CardPiece(true, true);
            RowHover = Make(null, Rounded(HoverOverlay, 6, null), 6);
            Card = Make(null, Rounded(CardBg, 8, CardBorder), 8);
            Card.padding = Offset(0, 0, U(4), U(4));
            Card.margin = Offset(0, U(4), U(2), U(10));
            TitleBar = new GUIStyle { padding = Offset(U(18), U(12), U(10), U(10)) };

            // ---------------------------------------------------------------- text
            Title = TextStyle(17, TextColor, FontStyle.Bold);
            PageTitle = TextStyle(22, TextColor, FontStyle.Bold);
            PageTitle.padding = Offset(U(2), U(2), 0, 0);
            // A long mod name wraps under itself rather than pushing the page's buttons off the window.
            PageTitle.wordWrap = true;
            PageSubtitle = TextStyle(12, MutedColor);
            PageSubtitle.wordWrap = true;
            // Headings in the scrolling lists carry padding, never margins: a heading scrolled out
            // of view is replaced by a Space of its measured height, and margins are not measured.
            SectionHeader = TextStyle(11, MutedColor, FontStyle.Bold);
            SectionHeader.margin = Offset(0);
            SectionHeader.padding = Offset(U(6), 0, compact ? U(12) : U(20), compact ? U(6) : U(8));
            GroupHeader = TextStyle(14, Accent, FontStyle.Bold);
            GroupHeader.margin = Offset(0);
            GroupHeader.padding = Offset(U(4), 0, compact ? U(12) : U(20), compact ? U(6) : U(8));
            GroupHeader.hover.textColor = Color.white;
            GroupHeaderDanger = new GUIStyle(GroupHeader);
            GroupHeaderDanger.normal.textColor = Danger;
            Label = TextStyle(14, TextColor);
            Label.wordWrap = true;
            Muted = TextStyle(13, MutedColor);
            Muted.wordWrap = true;
            Small = TextStyle(12, MutedColor);
            Small.wordWrap = true;
            Description = TextStyle(13, MutedColor);
            Description.wordWrap = true;
            Description.padding = Offset(U(2), U(2), 0, U(2));
            DangerText = TextStyle(12, Danger);
            DangerText.wordWrap = true;
            DangerText.hover.textColor = Color.Lerp(Danger, Color.white, 0.35f);

            Badge = Make(TextStyle(11, AccentText, FontStyle.Bold), Rounded(Accent, 8, null), 8);
            Badge.alignment = TextAnchor.MiddleCenter;
            Badge.padding = Offset(U(7), U(7), U(1), U(2));
            Badge.margin = Offset(U(4), 0, 0, 0);
            BadgeDanger = new GUIStyle(Badge);
            BadgeDanger.normal.background = Rounded(Danger, 8, null);
            BadgeMuted = new GUIStyle(Badge);
            BadgeMuted.normal.background = Rounded(BadgeMutedBg, 8, null);
            BadgeMuted.normal.textColor = SoftText;
            Tag = Make(TextStyle(10, MutedColor, FontStyle.Bold), Rounded(TagBg, 6, null), 6);
            Tag.padding = Offset(U(6), U(6), U(1), U(2));
            Tag.margin = Offset(U(2), U(4), U(2), U(2));

            // ---------------------------------------------------------------- sidebar
            SidebarItem = Make(null, null, 6);
            SidebarItem.hover.background = hover;
            SidebarItem.onNormal.background = selected;
            SidebarItem.onHover.background = selected;
            SidebarItem.fixedHeight = compact ? T(26) : T(32);
            SidebarItem.margin = Offset(0, 0, U(1), U(1));
            SidebarText = TextStyle(13, SoftText);
            SidebarText.padding = Offset(U(14), U(8), 0, 0);
            SidebarText.clipping = TextClipping.Clip;
            SidebarText.hover.textColor = TextColor;
            SidebarTextActive = new GUIStyle(SidebarText) { fontStyle = FontStyle.Bold };
            SidebarTextHidden = new GUIStyle(SidebarText) { fontStyle = FontStyle.Italic };
            SidebarTextHidden.normal.textColor = MutedColor;
            // This menu's own entry: bold, in the accent, so it stands out among the mods.
            SidebarTextOwn = new GUIStyle(SidebarText) { fontStyle = FontStyle.Bold };
            SidebarTextOwn.normal.textColor = Accent;
            SidebarTextOwn.hover.textColor = Color.Lerp(Accent, Color.white, 0.3f);
            SidebarTextActive.normal.textColor = TextColor;
            SidebarHeader = TextStyle(10, MutedColor, FontStyle.Bold);
            SidebarHeader.padding = Offset(U(14), 0, U(16), U(6));
            SidebarSelected = Make(null, selected, 6);
            SidebarToggle = TextStyle(10, Accent, FontStyle.Bold);
            SidebarToggle.padding = Offset(U(6), U(6), U(16), U(6));
            SidebarToggle.hover.textColor = Color.white;
            SidebarHiddenToggle = new GUIStyle(SidebarToggle) { alignment = TextAnchor.MiddleLeft };
            SidebarHiddenToggle.padding = Offset(U(14), U(6), U(2), U(8));

            // ---------------------------------------------------------------- buttons
            Button = Make(TextStyle(13, TextColor), Rounded(ButtonBg, 6, null), 6);
            Button.hover.background = Rounded(ButtonHover, 6, null);
            Button.active.background = Rounded(ButtonActive, 6, null);
            Button.hover.textColor = Color.white;
            Button.active.textColor = Color.white;
            Button.alignment = TextAnchor.MiddleCenter;
            Button.padding = Offset(U(14), U(14), U(6), U(7));
            Button.margin = Offset(U(3), U(3), U(2), U(2));
            ButtonAccent = new GUIStyle(Button);
            ButtonAccent.normal.background = Rounded(Accent, 6, null);
            ButtonAccent.hover.background = Rounded(Color.Lerp(Accent, Color.white, 0.18f), 6, null);
            ButtonAccent.active.background = Rounded(Color.Lerp(Accent, Color.black, 0.1f), 6, null);
            ButtonAccent.normal.textColor = AccentText;
            ButtonAccent.hover.textColor = AccentText;
            ButtonAccent.active.textColor = AccentText;
            ButtonAccent.fontStyle = FontStyle.Bold;
            ButtonDanger = new GUIStyle(Button);
            ButtonDanger.normal.background = Rounded(Danger, 6, null);
            ButtonDanger.hover.background = Rounded(Color.Lerp(Danger, Color.white, 0.15f), 6, null);
            ButtonDanger.normal.textColor = Color.white;
            ButtonDanger.fontStyle = FontStyle.Bold;

            // Ghost buttons: text only until hovered. The quiet style for secondary actions.
            Ghost = new GUIStyle(Button);
            Ghost.normal.background = null;
            Ghost.normal.textColor = SoftText;
            Ghost.hover.background = Rounded(ButtonBg, 6, null);
            // On: a ghost button in the accent, same size as the others, for a state that is set
            // (a favourite, a mod hidden from the list). A chip beside ghost buttons sat shorter.
            GhostOn = new GUIStyle(Ghost);
            GhostOn.normal.textColor = Accent;
            GhostOn.hover.textColor = Color.Lerp(Accent, Color.white, 0.3f);
            GhostDanger = new GUIStyle(Ghost);
            GhostDanger.normal.textColor = Danger;
            GhostDanger.hover.textColor = Color.white;
            GhostDanger.hover.background = Rounded(DangerBg, 6, null);
            IconButton = new GUIStyle(Ghost);
            IconButton.fontSize = FontSize(18);
            IconButton.padding = Offset(0, 0, 0, U(2));
            IconButton.fixedWidth = U(34);
            // The height of the fields it sits beside (search box, keybind field), so they line up.
            IconButton.fixedHeight = T(34);

            // A ghost button with a small switch drawn at its right end, for a setting that
            // applies everywhere (the title bar's Show advanced).
            HeaderToggle = new GUIStyle(Ghost);
            HeaderToggle.alignment = TextAnchor.MiddleLeft;
            HeaderToggle.padding = Offset(U(12), U(12) + U(40) + U(10), U(6), U(7));
            HeaderToggle.fixedHeight = T(34);

            // The × inside a search box that clears it.
            ClearGlyph = TextStyle(16, MutedColor);
            ClearGlyph.alignment = TextAnchor.MiddleCenter;
            ClearGlyph.padding = Offset(0, 0, 0, U(2));
            ClearGlyph.hover.textColor = TextColor;
            LinkButton = TextStyle(12, Accent);
            LinkButton.hover.textColor = Color.white;
            LinkButton.padding = Offset(U(6), U(2), U(2), U(2));
            Dropdown = new GUIStyle(Button);
            Dropdown.normal.background = Rounded(InputBg, 6, InputBorder);
            Dropdown.hover.background = Rounded(FieldHoverBg, 6, InputHoverBorder);
            Dropdown.alignment = TextAnchor.MiddleLeft;
            Dropdown.padding = Offset(U(12), U(28), U(6), U(7));
            Dropdown.clipping = TextClipping.Clip;
            Dropdown.margin = Offset(0, 0, U(2), U(2));

            // ---------------------------------------------------------------- inputs
            Texture2D input = Rounded(InputBg, 6, InputBorder);
            TextField = Make(TextStyle(13, TextColor), input, 6);
            TextField.hover.background = Rounded(InputBg, 6, InputHoverBorder);
            TextField.focused.background = Rounded(InputBg, 6, Accent);
            TextField.focused.textColor = Color.white;
            TextField.padding = Offset(U(10), U(10), U(6), U(7));
            TextField.margin = Offset(0, 0, U(2), U(2));
            TextField.clipping = TextClipping.Clip;
            SearchField = new GUIStyle(TextField);
            SearchField.normal.background = Rounded(SearchBg, 15, InputBorder);
            SearchField.hover.background = Rounded(SearchBg, 15, InputHoverBorder);
            SearchField.focused.background = Rounded(SearchBg, 15, Accent);
            SearchField.border = new RectOffset(R(15) + 1, R(15) + 1, R(15) + 1, R(15) + 1);
            SearchField.fontSize = FontSize(14);
            SearchField.fixedHeight = T(34);
            // Right padding leaves room for the clear × drawn inside the box.
            SearchField.padding = Offset(U(38), U(36), U(7), U(7));
            NumberField = new GUIStyle(TextField);
            NumberField.alignment = TextAnchor.MiddleRight;

            SidebarFilter = new GUIStyle(SearchField);
            SidebarFilter.fontSize = FontSize(13);
            SidebarFilter.fixedHeight = T(30);
            SidebarFilter.padding = Offset(U(34), U(30), U(6), U(6));
            KeyHint = TextStyle(13, MutedColor);
            KeyHintActive = TextStyle(13, Accent, FontStyle.Bold);
            KeyField = Make(TextStyle(13, MutedColor), input, 6);
            KeyField.hover.background = Rounded(FieldHoverBg, 6, InputHoverBorder);
            KeyField.padding = Offset(U(8), U(8), 0, 0);
            KeyField.margin = Offset(0, 0, U(2), U(2));
            KeyField.fixedHeight = T(34);
            KeyFieldActive = new GUIStyle(KeyField);
            KeyFieldActive.normal.background = Rounded(Color.Lerp(InputBg, Accent, 0.08f), 6, Accent);
            KeyFieldActive.hover.background = KeyFieldActive.normal.background;
            KeyFieldActive.normal.textColor = Accent;
            KeyFieldActive.hover.textColor = Accent;
            Keycap = Make(TextStyle(12, TextColor, FontStyle.Bold), Keycap3D(), 4);
            Keycap.alignment = TextAnchor.MiddleCenter;
            Keycap.padding = Offset(U(8), U(8), U(2), U(4));
            Keycap.fixedHeight = T(24);
            KeyPlus = TextStyle(12, MutedColor);
            KeyPlus.alignment = TextAnchor.MiddleCenter;

            // ---------------------------------------------------------------- toggles
            Chip = Make(TextStyle(12, SoftText), Rounded(ButtonBg, 12, null), 12);
            Chip.hover.background = Rounded(ButtonHover, 12, null);
            Chip.hover.textColor = TextColor;
            Chip.padding = Offset(U(12), U(12), U(4), U(5));
            Chip.margin = Offset(U(2), U(2), U(2), U(2));
            Chip.alignment = TextAnchor.MiddleCenter;
            ChipOn = new GUIStyle(Chip);
            ChipOn.normal.background = Rounded(AccentSoft, 12, Accent);
            ChipOn.hover.background = ChipOn.normal.background;
            ChipOn.normal.textColor = Accent;
            ChipOn.hover.textColor = Color.Lerp(Accent, Color.white, 0.3f);
            // The filter bar's chips: as tall as the mod's search box beside them, with its
            // margins and corners, so the two line up.
            FilterChip = new GUIStyle(Chip);
            FilterChip.normal.background = Rounded(ButtonBg, 15, null);
            FilterChip.hover.background = Rounded(ButtonHover, 15, null);
            FilterChip.border = SidebarFilter.border;
            FilterChip.fixedHeight = SidebarFilter.fixedHeight;
            FilterChip.margin = Offset(U(2), U(2), SidebarFilter.margin.top, SidebarFilter.margin.bottom);
            FilterChip.padding = Offset(U(14), U(14), 0, 0);
            FilterChipOn = new GUIStyle(FilterChip);
            FilterChipOn.normal.background = Rounded(AccentSoft, 15, Accent);
            FilterChipOn.hover.background = FilterChipOn.normal.background;
            FilterChipOn.normal.textColor = ChipOn.normal.textColor;
            FilterChipOn.hover.textColor = ChipOn.hover.textColor;
            TrackOn = Make(null, Rounded(Accent, 11, null), 11);
            TrackOff = Make(null, Rounded(TrackOffColor, 11, null), 11);

            // ---------------------------------------------------------------- rows, popups
            RowStyle = Make(null, null, 6);
            RowStyle.padding = Offset(U(16), U(14), U(11), U(11));
            RowStyle.margin = Offset(0);

            Popup = Make(null, Rounded(PopupBg, 8, PopupBorder), 8);
            Popup.padding = Offset(U(4), U(4), U(4), U(4));
            PopupItem = Make(TextStyle(13, SoftText), null, 6);
            PopupItem.hover.background = hover;
            PopupItem.hover.textColor = TextColor;
            PopupItem.padding = Offset(U(12), U(12), U(6), U(7));
            PopupItem.margin = Offset(0);
            PopupItemActive = new GUIStyle(PopupItem) { fontStyle = FontStyle.Bold };
            PopupItemActive.normal.textColor = Accent;
            PopupItemActive.hover.textColor = Accent;

            Status = TextStyle(12, MutedColor);
            Status.padding = Offset(U(18), U(18), U(6), U(6));
            Tooltip = Make(TextStyle(12, TextColor), Rounded(TooltipBg, 6, PopupBorder), 6);
            Tooltip.wordWrap = true;
            Tooltip.padding = Offset(U(12), U(12), U(8), U(9));

            // ---------------------------------------------------------------- sliders
            // The track and fill are drawn by the menu itself (so the fill can sit under the
            // thumb); the slider style only places the thumb. Unity's thumb rect is the control
            // rect inset by the slider's padding, so the padding sets the thumb's height.
            SliderTrack = Make(null, Rounded(TrackOffColor, 3, null), 3);
            SliderFill = Make(null, Rounded(Accent, 3, null), 3);
            Slider = new GUIStyle
            {
                fixedHeight = U(30),
                padding = Offset(0, 0, U(7), U(7)),
                margin = Offset(U(2), U(10), 0, 0),
            };
            SliderThumb = new GUIStyle { fixedWidth = U(16) };
            SliderThumb.normal.background = Circle(Accent, 16);
            SliderThumb.hover.background = Circle(Color.Lerp(Accent, Color.white, 0.25f), 16);
            SliderThumb.active.background = Circle(Color.Lerp(Accent, Color.white, 0.45f), 16);

            // ---------------------------------------------------------------- fallback skin
            // What every control falls back to, including the stock drawer's.
            GuiSkin.label = Label;
            GuiSkin.button = Button;
            GuiSkin.textField = TextField;
            GuiSkin.textArea = new GUIStyle(TextField) { wordWrap = true };
            GuiSkin.box = Card;
            GuiSkin.horizontalSlider = Make(null, Rounded(TrackOffColor, 3, null), 3);
            GuiSkin.horizontalSlider.fixedHeight = U(6);
            GuiSkin.horizontalSlider.margin = Offset(U(4), U(4), U(12), U(12));
            GuiSkin.horizontalSliderThumb = new GUIStyle(SliderThumb) { overflow = Offset(0, 0, U(5), U(5)) };
            GuiSkin.window = Window;
            GuiSkin.scrollView = new GUIStyle();
            GuiSkin.verticalScrollbar = Make(null, null, 4);
            GuiSkin.verticalScrollbar.fixedWidth = U(6);
            GuiSkin.verticalScrollbar.margin = Offset(U(4), U(2), U(2), U(2));
            GuiSkin.verticalScrollbarThumb = Make(null, Rounded(ScrollThumb, 3, null), 3);
            GuiSkin.verticalScrollbarThumb.fixedWidth = U(6);
            GuiSkin.verticalScrollbarUpButton = GUIStyle.none;
            GuiSkin.verticalScrollbarDownButton = GUIStyle.none;
            GuiSkin.horizontalScrollbar = GUIStyle.none;
            GuiSkin.horizontalScrollbarThumb = GUIStyle.none;
            GuiSkin.horizontalScrollbarLeftButton = GUIStyle.none;
            GuiSkin.horizontalScrollbarRightButton = GUIStyle.none;
            GuiSkin.toggle = new GUIStyle(Chip);
            GuiSkin.toggle.onNormal.background = ChipOn.normal.background;
            GuiSkin.toggle.onHover.background = ChipOn.normal.background;
            GuiSkin.toggle.onNormal.textColor = Accent;
            GuiSkin.toggle.onHover.textColor = Accent;
            GuiSkin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.4f);
            GuiSkin.settings.cursorColor = Accent;

            // Last, once every style has its font and padding: the corners of the short styles
            // are fitted to the size they are drawn at.
            foreach (GUIStyle s in new[] { Badge, BadgeDanger, BadgeMuted, Tag, Chip, ChipOn, FilterChip, FilterChipOn, GuiSkin.toggle, SearchField, SidebarFilter })
            {
                FitCorners(s, s.fixedHeight > 0f ? s.fixedHeight : s.CalcHeight(SampleText, 1000f), 0f);
            }
            FitCorners(TrackOn, U(24), 0f);
            FitCorners(TrackOff, U(24), 0f);
            FitCorners(SliderTrack, U(6), U(6));
            FitCorners(SliderFill, U(6), U(6));
            FitCorners(GuiSkin.horizontalSlider, U(6), 0f);
            FitCorners(GuiSkin.verticalScrollbarThumb, float.MaxValue, U(6));
            _refitted.Clear();
        }

        private static readonly GUIContent SampleText = new GUIContent("0");

        /// <summary>
        /// Shrinks a style's corners when its nine-slice borders do not fit the height (or width)
        /// it is drawn at. Unity then overlaps the top and bottom slices and paints the strip where
        /// they meet twice: invisible while the menu is opaque, a line through every badge and chip
        /// once it fades into the background. The corners become as round as the shape allows.
        /// </summary>
        private void FitCorners(GUIStyle s, float height, float width)
        {
            bool tooTall = s.border.vertical > height;
            bool tooWide = width > 0f && s.border.horizontal > width;
            if (!tooTall && !tooWide)
            {
                return;
            }
            int radius = Mathf.Max(1, Mathf.FloorToInt((width > 0f ? Mathf.Min(height, width) : height) / 2f) - 1);
            s.border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1);
            s.normal.background = Refit(s.normal.background, radius);
            s.hover.background = Refit(s.hover.background, radius);
            s.active.background = Refit(s.active.background, radius);
            s.focused.background = Refit(s.focused.background, radius);
            s.onNormal.background = Refit(s.onNormal.background, radius);
            s.onHover.background = Refit(s.onHover.background, radius);
            s.onActive.background = Refit(s.onActive.background, radius);
            s.onFocused.background = Refit(s.onFocused.background, radius);
        }

        /// <summary>The same rounded texture redrawn with a smaller radius; shared textures stay shared.</summary>
        private Texture2D Refit(Texture2D t, int radius)
        {
            if (t == null || !_rounded.TryGetValue(t, out RoundedSpec spec))
            {
                return t;
            }
            if (spec.Radius == radius)
            {
                return t;
            }
            long key = ((long)t.GetInstanceID() << 16) | (uint)radius;
            if (!_refitted.TryGetValue(key, out Texture2D fitted))
            {
                fitted = RoundedPx(spec.Fill, radius, spec.Outline);
                _refitted[key] = fitted;
            }
            return fitted;
        }

        private struct RoundedSpec
        {
            public Color Fill;
            public int Radius;
            public Color? Outline;
        }

        /// <summary>What each rounded texture was drawn from, so <see cref="FitCorners"/> can redraw it.</summary>
        private readonly Dictionary<Texture2D, RoundedSpec> _rounded = new Dictionary<Texture2D, RoundedSpec>();
        private readonly Dictionary<long, Texture2D> _refitted = new Dictionary<long, Texture2D>();

        public void Destroy()
        {
            foreach (Object o in _owned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }
            _owned.Clear();
            _rounded.Clear();
            _refitted.Clear();
            GuiSkin = null;
        }

        /// <summary>Draws a texture tinted, restoring GUI.color after.</summary>
        public void Fill(Rect r, Color color, Texture2D tex = null)
        {
            // Multiplied by the current alpha, so fills fade with the rest of the window.
            Color old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            GUI.DrawTexture(r, tex ?? WhiteTex);
            GUI.color = old;
        }

        private GUIStyle CardPiece(bool top, bool bottom)
        {
            GUIStyle s = Make(null, RoundedPart(CardBg, 8, CardBorder, top, bottom), 8);
            s.padding = Options.Compact ? Offset(U(16), U(14), U(6), U(6)) : Offset(U(18), U(16), U(12), U(12));
            s.margin = Offset(0);
            return s;
        }

        /// <summary>
        /// A corner radius (or texture size) in pixels at the current scale. Textures are drawn
        /// at the size they are shown, so corners stay as round and edges as sharp at 4K as at
        /// 1080p; the nine-slice borders use the same value, so they always match the texture.
        /// </summary>
        private int R(int basePx) => Mathf.Max(1, Mathf.RoundToInt(basePx * Scale));

        private int FontSize(int basePx) => Mathf.Max(8, Mathf.RoundToInt(basePx * Scale * Options.TextScale));

        /// <summary>Sets the static palette for a theme. The accent is the player's own colour.</summary>
        private static void ApplyPalette(MenuTheme theme, Color accent)
        {
            Danger = Hex(0xE06C60);
            DangerBg = Hex(0x3A1F1C);
            HoverOverlay = new Color(1f, 1f, 1f, 0.035f);
            switch (theme)
            {
                case MenuTheme.Black:
                    Set(window: 0x050506, outline: 0x1E2024, card: 0x0D0E10, cardBorder: 0x1B1D21, divider: 0x1A1C1F,
                        hover: 0x16181B, selected: 0x1A1C20, input: 0x000000, inputBorder: 0x25282D, inputHover: 0x343840,
                        fieldHover: 0x0A0B0C, search: 0x000000, button: 0x16181B, buttonHover: 0x202327, buttonActive: 0x2A2E33,
                        text: 0xECE8DF, soft: 0xBDC0C4, muted: 0x878C93, track: 0x2A2D33, keycap: 0x1F2226, keycapEdge: 0x2F333A,
                        keycapLip: 0x050506, tag: 0x1C1E22, badge: 0x22252A, scroll: 0x2C3036, popup: 0x0E0F11,
                        popupBorder: 0x2A2D33, tooltip: 0x000000);
                    break;
                case MenuTheme.Grey:
                    Set(window: 0x2B2E34, outline: 0x454A52, card: 0x34383F, cardBorder: 0x40454D, divider: 0x3D4249,
                        hover: 0x3D4249, selected: 0x40454D, input: 0x23262B, inputBorder: 0x4A4F58, inputHover: 0x5A606A,
                        fieldHover: 0x272A30, search: 0x23262B, button: 0x40454D, buttonHover: 0x4B515A, buttonActive: 0x575D67,
                        text: 0xF3F1EC, soft: 0xD5D8DC, muted: 0xA6ACB3, track: 0x50565F, keycap: 0x474C55, keycapEdge: 0x585E68,
                        keycapLip: 0x2A2D32, tag: 0x41464E, badge: 0x474C55, scroll: 0x555B64, popup: 0x30343A,
                        popupBorder: 0x4F555E, tooltip: 0x1E2024);
                    HoverOverlay = new Color(1f, 1f, 1f, 0.05f);
                    break;
                case MenuTheme.HighContrast:
                    Set(window: 0x000000, outline: 0x8A8A8A, card: 0x0B0B0B, cardBorder: 0x707070, divider: 0x4A4A4A,
                        hover: 0x222222, selected: 0x2C2C2C, input: 0x000000, inputBorder: 0xA0A0A0, inputHover: 0xDADADA,
                        fieldHover: 0x111111, search: 0x000000, button: 0x222222, buttonHover: 0x383838, buttonActive: 0x4A4A4A,
                        text: 0xFFFFFF, soft: 0xFFFFFF, muted: 0xD0D0D0, track: 0x5A5A5A, keycap: 0x262626, keycapEdge: 0x9A9A9A,
                        keycapLip: 0x000000, tag: 0x2A2A2A, badge: 0x333333, scroll: 0x8A8A8A, popup: 0x050505,
                        popupBorder: 0xA0A0A0, tooltip: 0x000000);
                    Danger = Hex(0xFF6E61);
                    HoverOverlay = new Color(1f, 1f, 1f, 0.08f);
                    break;
                case MenuTheme.Tan:
                    Set(window: 0x1A1814, outline: 0x3A342B, card: 0x221F1A, cardBorder: 0x332E26, divider: 0x2E2A22,
                        hover: 0x2B2720, selected: 0x302B23, input: 0x14120F, inputBorder: 0x3A342B, inputHover: 0x4A4337,
                        fieldHover: 0x181612, search: 0x12100D, button: 0x2D2821, buttonHover: 0x38322A, buttonActive: 0x433C32,
                        text: 0xEDE4D0, soft: 0xCFC4AE, muted: 0x9A8F7A, track: 0x463F35, keycap: 0x37312A, keycapEdge: 0x4A4337,
                        keycapLip: 0x12100D, tag: 0x2F2A22, badge: 0x37312A, scroll: 0x4A4337, popup: 0x211E19,
                        popupBorder: 0x40392F, tooltip: 0x0F0E0C);
                    break;
                default:
                    Set(window: 0x16181C, outline: 0x2E333A, card: 0x1D2025, cardBorder: 0x272B31, divider: 0x262A30,
                        hover: 0x252930, selected: 0x262A31, input: 0x111316, inputBorder: 0x2C3138, inputHover: 0x3A4048,
                        fieldHover: 0x181B1F, search: 0x0F1114, button: 0x262A30, buttonHover: 0x30353C, buttonActive: 0x3A4048,
                        text: 0xECE8DF, soft: 0xC2C5C9, muted: 0x8E949B, track: 0x3A3F46, keycap: 0x2B3037, keycapEdge: 0x3B424B,
                        keycapLip: 0x15181C, tag: 0x2A2E34, badge: 0x2C3137, scroll: 0x3A4047, popup: 0x1C1F24,
                        popupBorder: 0x353B43, tooltip: 0x0C0D0F);
                    break;
            }

            accent.a = 1f;
            Accent = accent;
            // Dark text on a light accent, white on a dark one, by perceived brightness.
            float luminance = 0.299f * accent.r + 0.587f * accent.g + 0.114f * accent.b;
            AccentText = luminance > 0.55f ? Hex(0x17140C) : Color.white;
            AccentSoft = Color.Lerp(CardBg, accent, 0.18f);
        }

        private static void Set(int window, int outline, int card, int cardBorder, int divider, int hover, int selected,
            int input, int inputBorder, int inputHover, int fieldHover, int search, int button, int buttonHover,
            int buttonActive, int text, int soft, int muted, int track, int keycap, int keycapEdge, int keycapLip,
            int tag, int badge, int scroll, int popup, int popupBorder, int tooltip)
        {
            WindowBg = Hex(window);
            WindowOutline = Hex(outline);
            CardBg = Hex(card);
            CardBorder = Hex(cardBorder);
            Divider = Hex(divider);
            Hover = Hex(hover);
            Selected = Hex(selected);
            InputBg = Hex(input);
            InputBorder = Hex(inputBorder);
            InputHoverBorder = Hex(inputHover);
            FieldHoverBg = Hex(fieldHover);
            SearchBg = Hex(search);
            ButtonBg = Hex(button);
            ButtonHover = Hex(buttonHover);
            ButtonActive = Hex(buttonActive);
            TextColor = Hex(text);
            SoftText = Hex(soft);
            MutedColor = Hex(muted);
            TrackOffColor = Hex(track);
            KeycapBg = Hex(keycap);
            KeycapEdge = Hex(keycapEdge);
            KeycapLip = Hex(keycapLip);
            TagBg = Hex(tag);
            BadgeMutedBg = Hex(badge);
            ScrollThumb = Hex(scroll);
            PopupBg = Hex(popup);
            PopupBorder = Hex(popupBorder);
            TooltipBg = Hex(tooltip, 0.97f);
        }

        private GUIStyle TextStyle(int size, Color color, FontStyle style = FontStyle.Normal)
        {
            GUIStyle s = new GUIStyle
            {
                font = _font,
                fontSize = FontSize(size),
                fontStyle = style,
                richText = false,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft,
                padding = Offset(U(2), U(2), U(2), U(2)),
                margin = Offset(U(2), U(2), U(2), U(2)),
            };
            s.normal.textColor = color;
            s.hover.textColor = color;
            s.active.textColor = color;
            s.focused.textColor = color;
            s.onNormal.textColor = color;
            s.onHover.textColor = color;
            return s;
        }

        private GUIStyle Make(GUIStyle from, Texture2D background, int radius)
        {
            GUIStyle s = from ?? new GUIStyle { font = _font };
            s.normal.background = background;
            // Set even with no normal background: hover and active states added later share the
            // style's one border, and a rounded hover texture without it stretches its corners.
            if (radius > 0)
            {
                int b = R(radius) + 1;
                s.border = new RectOffset(b, b, b, b);
            }
            return s;
        }

        private static RectOffset Offset(float all) => new RectOffset((int)all, (int)all, (int)all, (int)all);

        private static RectOffset Offset(float left, float right, float top, float bottom)
        {
            return new RectOffset((int)left, (int)right, (int)top, (int)bottom);
        }

        private Texture2D NewTexture(int w, int h)
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _owned.Add(t);
            return t;
        }

        private Texture2D Solid(Color color)
        {
            Texture2D t = NewTexture(1, 1);
            t.SetPixel(0, 0, color);
            t.Apply();
            return t;
        }

        /// <summary>
        /// A rounded rectangle with soft (coverage-sampled) edges, sized so nine-slicing with a
        /// border of radius + 1 keeps the corners intact at any size.
        /// </summary>
        private Texture2D Rounded(Color fill, int radius, Color? outline) => RoundedPx(fill, R(radius), outline);

        /// <summary>A rounded rectangle with a radius in pixels, already scaled.</summary>
        private Texture2D RoundedPx(Color fill, int radius, Color? outline)
        {
            int size = radius * 2 + 4;
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = CornerDistance(x + 0.5f, y + 0.5f, size, radius);
                    float coverage = radius == 0 ? 1f : Mathf.Clamp01(radius - d + 0.5f);
                    Color c = fill;
                    if (outline.HasValue)
                    {
                        float inner = Mathf.Clamp01(radius - 1f - d + 0.5f);
                        c = Color.Lerp(fill, outline.Value, 1f - inner);
                    }
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            _rounded[t] = new RoundedSpec { Fill = fill, Radius = radius, Outline = outline };
            return t;
        }

        /// <summary>
        /// A rounded rectangle with only some corners rounded and outlined: a card's top piece has
        /// round top corners and no bottom edge, so stacked pieces join into one card.
        /// </summary>
        private Texture2D RoundedPart(Color fill, int radius, Color outline, bool roundTop, bool roundBottom)
        {
            radius = R(radius);
            int size = radius * 2 + 4;
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            // Texture rows run bottom-up: y == 0 is the bottom edge.
            float lo = roundBottom ? radius : -10000f;
            float hi = roundTop ? size - radius : 10000f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float dx = px - Mathf.Clamp(px, radius, size - radius);
                    float dy = py - Mathf.Clamp(py, lo, hi);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float coverage = Mathf.Clamp01(radius - d + 0.5f);
                    float inner = Mathf.Clamp01(radius - 1f - d + 0.5f);
                    Color c = Color.Lerp(fill, outline, 1f - inner);
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        /// <summary>A keycap: rounded, with a darker lip along the bottom edge.</summary>
        private Texture2D Keycap3D()
        {
            int radius = R(4);
            int size = radius * 2 + 4;
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = CornerDistance(x + 0.5f, y + 0.5f, size, radius);
                    float coverage = Mathf.Clamp01(radius - d + 0.5f);
                    // Texture rows run bottom-up, so y == 0 is the bottom edge.
                    Color c = y < 2 ? KeycapLip : Color.Lerp(KeycapBg, KeycapEdge, 1f - Mathf.Clamp01(radius - 1f - d + 0.5f));
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        private Texture2D Circle(Color fill, int diameter)
        {
            diameter = R(diameter);
            int size = diameter + 2;
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            float c = size / 2f;
            float r = diameter / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c;
                    float dy = y + 0.5f - c;
                    Color p = fill;
                    p.a *= Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = p;
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        /// <summary>A search glass: a ring and a handle, white, tinted when drawn.</summary>
        private Texture2D Magnifier(int size)
        {
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            Vector2 centre = new Vector2(size * 0.42f, size * 0.58f);
            float radius = size * 0.27f;
            float ring = size * 0.075f;
            Vector2 a = centre + new Vector2(radius * 0.72f, -radius * 0.72f);
            Vector2 b = new Vector2(size * 0.86f, size * 0.14f);
            float handle = size * 0.085f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float ringCoverage = Mathf.Clamp01(ring - Mathf.Abs((p - centre).magnitude - radius) + 0.5f);
                    float handleCoverage = Mathf.Clamp01(handle - DistanceToSegment(p, a, b) + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(ringCoverage, handleCoverage));
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        /// <summary>
        /// A soft drop shadow: black, fading out over <paramref name="spread"/> pixels around a
        /// rounded core. Nine-sliced with a border of the spread, it fits any window size.
        /// </summary>
        private Texture2D Shadow(int spread)
        {
            int size = spread * 2 + 4;
            Texture2D t = NewTexture(size, size);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = CornerDistance(x + 0.5f, y + 0.5f, size, spread);
                    float f = Mathf.Clamp01(1f - d / spread);
                    pixels[y * size + x] = new Color(0f, 0f, 0f, 0.55f * f * f);
                }
            }
            t.SetPixels(pixels);
            t.Apply();
            return t;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * k)).magnitude;
        }

        /// <summary>Distance from the rectangle inset by the radius: 0 inside it, the radius on the outer edge.</summary>
        private static float CornerDistance(float x, float y, int size, int radius)
        {
            float cx = Mathf.Clamp(x, radius, size - radius);
            float cy = Mathf.Clamp(y, radius, size - radius);
            float dx = x - cx;
            float dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Color Hex(int rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }
    }
}
