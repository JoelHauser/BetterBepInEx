using System.Collections.Generic;
using UnityEngine;

namespace BetterBepInEx
{
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
        public static readonly Color WindowBg = Hex(0x16181C);
        public static readonly Color SidebarBg = Hex(0x121417);
        public static readonly Color CardBg = Hex(0x1D2025);
        public static readonly Color CardBorder = Hex(0x272B31);
        public static readonly Color Divider = Hex(0x262A30);
        public static readonly Color Hover = Hex(0x252930);
        public static readonly Color Selected = Hex(0x262A31);
        public static readonly Color InputBg = Hex(0x111316);
        public static readonly Color InputBorder = Hex(0x2C3138);
        public static readonly Color ButtonBg = Hex(0x262A30);
        public static readonly Color ButtonHover = Hex(0x30353C);
        public static readonly Color ButtonActive = Hex(0x3A4048);
        public static readonly Color TextColor = Hex(0xECE8DF);
        public static readonly Color SoftText = Hex(0xC2C5C9);
        public static readonly Color MutedColor = Hex(0x8E949B);
        public static readonly Color Accent = Hex(0xD6B76E);
        public static readonly Color AccentText = Hex(0x17140C);
        public static readonly Color Danger = Hex(0xE06C60);
        public static readonly Color DangerBg = Hex(0x3A1F1C);
        public static readonly Color TrackOffColor = Hex(0x3A3F46);
        public static readonly Color KeycapBg = Hex(0x2B3037);
        public static readonly Color KeycapEdge = Hex(0x3B424B);

        public float Scale { get; private set; }
        public Font Font => _font;
        public GUISkin GuiSkin { get; private set; }

        public GUIStyle Window, Sidebar, Card, TitleBar;
        public GUIStyle Title, PageTitle, PageSubtitle, SectionHeader, GroupHeader, GroupHeaderDanger;
        public GUIStyle Label, Muted, Small, Description, DangerText, Badge, BadgeDanger, BadgeMuted, Tag;
        public GUIStyle SidebarItem, SidebarText, SidebarTextActive, SidebarTextHidden, SidebarHeader, SidebarSelected, SidebarToggle, SidebarFilter, SidebarHiddenToggle;
        public GUIStyle CardTop, CardMid, CardBottom, CardSingle, RowHover, ShadowStyle, KeyHint, KeyHintActive;
        public GUIStyle Button, ButtonAccent, ButtonDanger, Ghost, GhostDanger, IconButton, LinkButton, Dropdown;
        public GUIStyle TextField, SearchField, NumberField, KeyField, KeyFieldActive, Keycap, KeyPlus;
        public GUIStyle Chip, ChipOn, TrackOn, TrackOff;
        public GUIStyle RowStyle, Popup, PopupItem, PopupItemActive, Status, Tooltip;
        public GUIStyle Slider, SliderThumb, SliderTrack, SliderFill;

        public Texture2D WhiteTex, KnobTex, SearchIcon, ShadowTex;

        /// <summary>Nine-slice border of <see cref="ShadowTex"/>, in texture pixels.</summary>
        public int ShadowBorder { get; private set; }

        private readonly List<Object> _owned = new List<Object>();
        private Font _font;

        public float U(float v) => Mathf.Round(v * Scale);

        public void Build(float scale, Font font)
        {
            Destroy();
            Scale = scale;
            _font = font;

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
            Window = Make(null, Rounded(WindowBg, 10, Hex(0x2E333A)), 10);
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
            RowHover = Make(null, Rounded(new Color(1f, 1f, 1f, 0.035f), 6, null), 6);
            Card = Make(null, Rounded(CardBg, 8, CardBorder), 8);
            Card.padding = Offset(0, 0, U(4), U(4));
            Card.margin = Offset(0, U(4), U(2), U(10));
            TitleBar = new GUIStyle { padding = Offset(U(18), U(12), U(10), U(10)) };

            // ---------------------------------------------------------------- text
            Title = TextStyle(17, TextColor, FontStyle.Bold);
            PageTitle = TextStyle(22, TextColor, FontStyle.Bold);
            PageTitle.padding = Offset(U(2), U(2), 0, 0);
            PageSubtitle = TextStyle(12, MutedColor);
            PageSubtitle.wordWrap = true;
            // Headings in the scrolling lists carry padding, never margins: a heading scrolled out
            // of view is replaced by a Space of its measured height, and margins are not measured.
            SectionHeader = TextStyle(11, MutedColor, FontStyle.Bold);
            SectionHeader.margin = Offset(0);
            SectionHeader.padding = Offset(U(6), 0, U(20), U(8));
            GroupHeader = TextStyle(14, Accent, FontStyle.Bold);
            GroupHeader.margin = Offset(0);
            GroupHeader.padding = Offset(U(4), 0, U(20), U(8));
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
            BadgeMuted.normal.background = Rounded(Hex(0x2C3137), 8, null);
            BadgeMuted.normal.textColor = SoftText;
            Tag = Make(TextStyle(10, MutedColor, FontStyle.Bold), Rounded(Hex(0x2A2E34), 6, null), 6);
            Tag.padding = Offset(U(6), U(6), U(1), U(2));
            Tag.margin = Offset(U(2), U(4), U(2), U(2));

            // ---------------------------------------------------------------- sidebar
            SidebarItem = Make(null, null, 6);
            SidebarItem.hover.background = hover;
            SidebarItem.onNormal.background = selected;
            SidebarItem.onHover.background = selected;
            SidebarItem.fixedHeight = U(32);
            SidebarItem.margin = Offset(0, 0, U(1), U(1));
            SidebarText = TextStyle(13, SoftText);
            SidebarText.padding = Offset(U(14), U(8), 0, 0);
            SidebarText.clipping = TextClipping.Clip;
            SidebarText.hover.textColor = TextColor;
            SidebarTextActive = new GUIStyle(SidebarText) { fontStyle = FontStyle.Bold };
            SidebarTextHidden = new GUIStyle(SidebarText) { fontStyle = FontStyle.Italic };
            SidebarTextHidden.normal.textColor = MutedColor;
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
            GhostDanger = new GUIStyle(Ghost);
            GhostDanger.normal.textColor = Danger;
            GhostDanger.hover.textColor = Color.white;
            GhostDanger.hover.background = Rounded(DangerBg, 6, null);
            IconButton = new GUIStyle(Ghost);
            IconButton.fontSize = FontSize(18);
            IconButton.padding = Offset(0, 0, 0, U(2));
            IconButton.fixedWidth = U(34);
            IconButton.fixedHeight = U(32);
            LinkButton = TextStyle(12, Accent);
            LinkButton.hover.textColor = Color.white;
            LinkButton.padding = Offset(U(6), U(2), U(2), U(2));
            Dropdown = new GUIStyle(Button);
            Dropdown.normal.background = Rounded(InputBg, 6, InputBorder);
            Dropdown.hover.background = Rounded(Hex(0x181B1F), 6, Hex(0x3A4048));
            Dropdown.alignment = TextAnchor.MiddleLeft;
            Dropdown.padding = Offset(U(12), U(28), U(6), U(7));
            Dropdown.clipping = TextClipping.Clip;
            Dropdown.margin = Offset(0, 0, U(2), U(2));

            // ---------------------------------------------------------------- inputs
            Texture2D input = Rounded(InputBg, 6, InputBorder);
            TextField = Make(TextStyle(13, TextColor), input, 6);
            TextField.hover.background = Rounded(InputBg, 6, Hex(0x3A4048));
            TextField.focused.background = Rounded(InputBg, 6, Accent);
            TextField.focused.textColor = Color.white;
            TextField.padding = Offset(U(10), U(10), U(6), U(7));
            TextField.margin = Offset(0, 0, U(2), U(2));
            TextField.clipping = TextClipping.Clip;
            SearchField = new GUIStyle(TextField);
            SearchField.normal.background = Rounded(Hex(0x0F1114), 15, InputBorder);
            SearchField.hover.background = Rounded(Hex(0x0F1114), 15, Hex(0x3A4048));
            SearchField.focused.background = Rounded(Hex(0x0F1114), 15, Accent);
            SearchField.border = new RectOffset(R(15) + 1, R(15) + 1, R(15) + 1, R(15) + 1);
            SearchField.fontSize = FontSize(14);
            SearchField.fixedHeight = U(34);
            SearchField.padding = Offset(U(38), U(14), U(7), U(7));
            NumberField = new GUIStyle(TextField);
            NumberField.alignment = TextAnchor.MiddleRight;
            SidebarFilter = new GUIStyle(SearchField);
            SidebarFilter.fontSize = FontSize(13);
            SidebarFilter.fixedHeight = U(30);
            SidebarFilter.padding = Offset(U(34), U(12), U(6), U(6));
            KeyHint = TextStyle(13, MutedColor);
            KeyHintActive = TextStyle(13, Accent, FontStyle.Bold);
            KeyField = Make(TextStyle(13, MutedColor), input, 6);
            KeyField.hover.background = Rounded(Hex(0x181B1F), 6, Hex(0x3A4048));
            KeyField.padding = Offset(U(8), U(8), 0, 0);
            KeyField.margin = Offset(0, 0, U(2), U(2));
            KeyField.fixedHeight = U(34);
            KeyFieldActive = new GUIStyle(KeyField);
            KeyFieldActive.normal.background = Rounded(Hex(0x1E1B12), 6, Accent);
            KeyFieldActive.hover.background = KeyFieldActive.normal.background;
            KeyFieldActive.normal.textColor = Accent;
            KeyFieldActive.hover.textColor = Accent;
            Keycap = Make(TextStyle(12, TextColor, FontStyle.Bold), Keycap3D(), 4);
            Keycap.alignment = TextAnchor.MiddleCenter;
            Keycap.padding = Offset(U(8), U(8), U(2), U(4));
            Keycap.fixedHeight = U(24);
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
            ChipOn.normal.background = Rounded(Hex(0x3A3220), 12, Accent);
            ChipOn.hover.background = ChipOn.normal.background;
            ChipOn.normal.textColor = Accent;
            ChipOn.hover.textColor = Color.Lerp(Accent, Color.white, 0.3f);
            TrackOn = Make(null, Rounded(Accent, 11, null), 11);
            TrackOff = Make(null, Rounded(TrackOffColor, 11, null), 11);

            // ---------------------------------------------------------------- rows, popups
            RowStyle = Make(null, null, 6);
            RowStyle.padding = Offset(U(16), U(14), U(11), U(11));
            RowStyle.margin = Offset(0);

            Popup = Make(null, Rounded(Hex(0x1C1F24), 8, Hex(0x353B43)), 8);
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
            Tooltip = Make(TextStyle(12, TextColor), Rounded(Hex(0x0C0D0F, 0.97f), 6, Hex(0x353B43)), 6);
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
            GuiSkin.verticalScrollbarThumb = Make(null, Rounded(Hex(0x3A4047), 3, null), 3);
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
        }

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
            s.padding = Offset(U(18), U(16), U(12), U(12));
            s.margin = Offset(0);
            return s;
        }

        /// <summary>
        /// A corner radius (or texture size) in pixels at the current scale. Textures are drawn
        /// at the size they are shown, so corners stay as round and edges as sharp at 4K as at
        /// 1080p; the nine-slice borders use the same value, so they always match the texture.
        /// </summary>
        private int R(int basePx) => Mathf.Max(1, Mathf.RoundToInt(basePx * Scale));

        private int FontSize(int basePx) => Mathf.Max(8, Mathf.RoundToInt(basePx * Scale));

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
        private Texture2D Rounded(Color fill, int radius, Color? outline)
        {
            radius = R(radius);
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
                    Color c = y < 2 ? Hex(0x15181C) : Color.Lerp(KeycapBg, KeycapEdge, 1f - Mathf.Clamp01(radius - 1f - d + 0.5f));
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
