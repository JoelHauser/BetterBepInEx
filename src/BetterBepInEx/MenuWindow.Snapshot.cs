using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>
    /// The Screenshot button on a mod's page: a picture of every setting the page shows, with the
    /// changed ones marked, for sending to whoever asked for your config. It only reads settings.
    ///
    /// The picture is drawn with the menu's own skin, over the whole screen in place of the menu,
    /// for a frame, then read back off the screen at the end of that frame. A list taller than the
    /// screen is drawn a screenful at a time over a few frames and stitched together.
    /// </summary>
    internal sealed partial class MenuWindow
    {
        private static readonly GUIContent ScreenshotLabel = new GUIContent("Screenshot",
            "Save a picture of this mod's settings and copy it, ready to paste into Discord");

        private static readonly object SnapshotPillKey = new object();

        /// <summary>Above this height the picture is split into several images, cut between rows.</summary>
        private const int SnapshotMaxPart = 8000;

        private enum SnapItemKind
        {
            Text,
            Row,
        }

        private struct SnapItem
        {
            public SnapItemKind Kind;
            public GUIContent Content;
            public GUIStyle Style;
            public Row Row;
            public GUIContent Default;
            public bool First;
            public bool Last;
            public float Y;
            public float H;
            public float NameH;
            public float ValueH;
            public int Part;
        }

        private sealed class Snapshot
        {
            public Page Page;
            public bool LaidOut;
            public readonly List<SnapItem> Items = new List<SnapItem>();
            public readonly List<Vector2> Parts = new List<Vector2>();
            public int Width;
            public float Pad;
            public float ValueWidth;
            public GUIStyle ValueStyle;
            public GUIStyle NumberStyle;

            // The screenful to draw this frame, and the frame it was drawn in.
            public int Part;
            public int TileTop;
            public int TileHeight;
            public int DrawnFrame = -1;
            public bool Failed;
        }

        private Snapshot _snapshot;

        private bool CanSnapshot(Page page)
        {
            if (_snapshot != null)
            {
                return false;
            }
            foreach (Row row in page.Rows)
            {
                if (SnapshotShows(row))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The rows the mod's page lists with no filter or search: hidden and advanced ones only with Show advanced.</summary>
        private bool SnapshotShows(Row row) => ShowHidden || !row.IsHidden && !row.IsAdvanced;

        private void StartSnapshot(Page page)
        {
            CommitEdit();
            ClosePopup();
            _plugin.StartCoroutine(TakeSnapshot(page));
        }

        private IEnumerator TakeSnapshot(Page page)
        {
            Snapshot s = new Snapshot { Page = page };
            // Starts on a frame of its own, not partway through the click's.
            yield return null;
            _snapshot = s;
            List<string> files = new List<string>();
            Texture2D texture = null;
            bool copied = false;
            string failure = null;
            try
            {
                // The first part is laid out on the first draw; the rest follow from it.
                for (int part = 0; failure == null && (part == 0 || part < s.Parts.Count); part++)
                {
                    s.Part = part;
                    // The first part's first screenful is sized when it is laid out, on its first draw.
                    s.TileTop = part == 0 ? 0 : (int)s.Parts[part].x;
                    s.TileHeight = part == 0 ? Screen.height : Mathf.Min(Screen.height, (int)(s.Parts[part].y - s.Parts[part].x));
                    while (true)
                    {
                        yield return new WaitForEndOfFrame();
                        if (s.Failed || s.DrawnFrame != Time.frameCount)
                        {
                            failure = s.Failed ? "it could not be drawn (the BepInEx log has the error)" : "the menu was closed while it was taken";
                            break;
                        }
                        Vector2 range = s.Parts[part];
                        int partTop = (int)range.x;
                        int partHeight = (int)(range.y - range.x);
                        if (texture == null || texture.height != partHeight)
                        {
                            if (texture != null)
                            {
                                UnityEngine.Object.Destroy(texture);
                            }
                            texture = new Texture2D(s.Width, partHeight, TextureFormat.RGB24, false);
                        }
                        // Screen pixels count up from the bottom; the tile is the top TileHeight rows.
                        int below = partTop + partHeight - s.TileTop - s.TileHeight;
                        texture.ReadPixels(new Rect(0, Screen.height - s.TileHeight, s.Width, s.TileHeight), 0, below);

                        int next = s.TileTop + s.TileHeight;
                        if (next >= partTop + partHeight)
                        {
                            break;
                        }
                        s.TileTop = next;
                        s.TileHeight = Mathf.Min(Screen.height, partTop + partHeight - next);
                    }
                    if (failure != null)
                    {
                        break;
                    }
                    failure = SaveSnapshotPart(texture, SnapshotFile(page, part, s.Parts.Count), s.Parts.Count == 1, files, ref copied);
                }
            }
            finally
            {
                // Also on an exception thrown above, or the coroutine stopped: the menu comes back.
                _snapshot = null;
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            if (failure != null || files.Count == 0)
            {
                SetStatus("No screenshot: " + (failure ?? "nothing was saved") + ".", true);
                yield break;
            }
            string folder = SnapshotFolderShown;
            if (files.Count > 1)
            {
                SetStatus("Saved as " + files.Count + " pictures in " + folder + ", opened for you.");
                ShowInExplorer(files[0]);
            }
            else if (copied)
            {
                SetStatus("Screenshot copied: paste it with Ctrl+V. Also saved in " + folder + ".");
            }
            else
            {
                SetStatus("Screenshot saved in " + folder + ".");
                ShowInExplorer(files[0]);
            }
            BetterBepInExPlugin.Log.LogInfo("Saved a screenshot of " + page.DisplayName + "'s settings: " + string.Join(", ", files.ToArray()));
        }

        /// <summary>Writes one picture, and copies it when it is the only one. Returns why it failed, or null.</summary>
        private static string SaveSnapshotPart(Texture2D texture, string file, bool copy, List<string> files, ref bool copied)
        {
            try
            {
                byte[] png = texture.EncodeToPNG();
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, png);
                files.Add(file);
                if (copy)
                {
                    copied = ClipboardImage.Copy(texture, png);
                }
                return null;
            }
            catch (Exception e)
            {
                BetterBepInExPlugin.Log.LogError("Could not save a screenshot to " + file + ": " + e);
                return "it could not be saved (the BepInEx log has the error)";
            }
        }

        private const string SnapshotFolderShown = @"BepInEx\BetterBepInEx\Screenshots";

        private static string SnapshotFile(Page page, int part, int parts)
        {
            string name = page.DisplayName;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            string suffix = parts > 1 ? " (" + (part + 1) + " of " + parts + ")" : string.Empty;
            return Path.Combine(Path.Combine(Path.Combine(Paths.BepInExRootPath, "BetterBepInEx"), "Screenshots"),
                name + " settings " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + suffix + ".png");
        }

        private static void ShowInExplorer(string file)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + file + "\"");
            }
            catch (Exception e)
            {
                BetterBepInExPlugin.Log.LogWarning("Could not open the screenshot's folder: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Draws this frame's screenful of the picture over the whole screen, on Repaint.</summary>
        private void DrawSnapshot()
        {
            Snapshot s = _snapshot;
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (!s.LaidOut)
            {
                LayoutSnapshot(s);
                s.TileHeight = Mathf.Min(Screen.height, (int)(s.Parts[0].y - s.Parts[0].x));
            }
            GUI.color = Color.white;
            GUI.enabled = true;
            // The whole screen, so the moment it shows reads as a flash rather than a glitch.
            _skin.Fill(new Rect(0, 0, Screen.width, Screen.height), Skin.WindowBg);

            float top = s.TileTop;
            float bottom = s.TileTop + s.TileHeight;
            foreach (SnapItem item in s.Items)
            {
                if (item.Part != s.Part || item.Y + item.H <= top || item.Y >= bottom)
                {
                    continue;
                }
                float y = item.Y - top;
                if (item.Kind == SnapItemKind.Text)
                {
                    item.Style.Draw(new Rect(s.Pad, y, s.Width - s.Pad * 2f, item.H), item.Content, false, false, false, false);
                }
                else
                {
                    DrawSnapshotRow(s, item, y);
                }
            }
            s.DrawnFrame = Time.frameCount;
        }

        private void DrawSnapshotRow(Snapshot s, SnapItem item, float y)
        {
            Row row = item.Row;
            GUIStyle card = CardStyle(item.First, item.Last);
            Rect r = new Rect(s.Pad, y, s.Width - s.Pad * 2f, item.H);
            card.Draw(r, false, false, false, false);
            if (!item.First)
            {
                _skin.Fill(new Rect(r.x + U(16), r.y, r.width - U(32), 1), Skin.Divider);
            }
            if (row.IsChanged)
            {
                float h = Mathf.Max(U(16), r.height - U(28));
                _skin.Fill(new Rect(r.x + U(5), r.y + Mathf.Round((r.height - h) / 2f), U(3), h), Skin.Accent);
            }

            float textWidth = SnapshotTextWidth(s, card);
            Rect name = new Rect(r.x + card.padding.left, r.y + card.padding.top, textWidth, item.NameH);
            _skin.Label.Draw(name, row.NamePlain, false, false, false, false);
            if (item.Default != null)
            {
                _skin.Small.Draw(new Rect(name.x, name.yMax, textWidth, item.H - card.padding.vertical - item.NameH), item.Default, false, false, false, false);
            }

            Rect value = new Rect(r.xMax - card.padding.right - s.ValueWidth, r.y + card.padding.top, s.ValueWidth, r.height - card.padding.vertical);
            float mid = value.y + Mathf.Round((value.height - item.ValueH) / 2f);
            switch (row.Kind)
            {
                case RowKind.Bool:
                    _switchAnim.Remove(SnapshotPillKey);
                    DrawPill(new Rect(value.xMax - U(46), mid, U(46), U(26)), row.Setting.Get() is bool b && b, SnapshotPillKey, false);
                    _switchAnim.Remove(SnapshotPillKey);
                    break;
                case RowKind.Color:
                    Color color = row.Setting.Get() is Color c ? c : Color.clear;
                    color.a = 1f;
                    float box = item.ValueH;
                    _skin.Fill(new Rect(value.x, mid, box, box), Skin.CardBorder);
                    _skin.Fill(new Rect(value.x + 1, mid + 1, box - 2, box - 2), color);
                    s.ValueStyle.Draw(new Rect(value.x + box + U(8), mid, value.width - box - U(8), item.ValueH), item.Content, false, false, false, false);
                    break;
                case RowKind.Range:
                case RowKind.Number:
                    float width = Mathf.Min(U(140), value.width);
                    s.NumberStyle.Draw(new Rect(value.xMax - width, mid, width, item.ValueH), item.Content, false, false, false, false);
                    break;
                default:
                    s.ValueStyle.Draw(new Rect(value.x, mid, value.width, item.ValueH), item.Content, false, false, false, false);
                    break;
            }
        }

        // ------------------------------------------------------------------ layout

        private float SnapshotTextWidth(Snapshot s, GUIStyle card) => s.Width - s.Pad * 2f - card.padding.horizontal - U(20) - s.ValueWidth;

        private void LayoutSnapshot(Snapshot s)
        {
            Page page = s.Page;
            s.LaidOut = true;
            s.Width = Mathf.Min((int)U(820), Screen.width);
            s.Pad = U(28);
            float inner = s.Width - s.Pad * 2f;
            s.ValueWidth = Mathf.Round(Mathf.Clamp(inner * 0.4f, U(180), U(320)));
            s.ValueStyle = new GUIStyle(_skin.TextField) { wordWrap = true, fixedHeight = 0f };
            s.NumberStyle = new GUIStyle(_skin.NumberField) { wordWrap = false, fixedHeight = 0f };

            float y = s.Pad;
            AddSnapText(s, ref y, page.Title, _skin.PageTitle, inner);
            if (page.Subtitle != null && !string.IsNullOrEmpty(page.Subtitle.text))
            {
                AddSnapText(s, ref y, new GUIContent(page.Subtitle.text), _skin.PageSubtitle, inner);
            }

            int shown = 0;
            int changed = 0;
            int left = 0;
            foreach (Row row in page.Rows)
            {
                if (!SnapshotShows(row))
                {
                    left++;
                }
                else
                {
                    shown++;
                    changed += row.IsChanged ? 1 : 0;
                }
            }
            y += U(6);
            string summary = shown + (shown == 1 ? " setting, " : " settings, ")
                             + (changed == 0 ? "all at their defaults." : changed + " changed from default (marked with a bar).")
                             + (left > 0 ? "  " + left + " advanced not shown." : string.Empty);
            AddSnapText(s, ref y, new GUIContent(summary), _skin.Muted, inner);

            bool sectionNames = page.Sections.Count > 1 || page.Sections.Count == 1 && page.Sections[0].Name.Length > 0;
            foreach (Section section in page.Sections)
            {
                List<Row> rows = section.Rows.FindAll(SnapshotShows);
                if (rows.Count == 0)
                {
                    continue;
                }
                if (sectionNames && section.Name.Length > 0)
                {
                    AddSnapText(s, ref y, section.Label, _skin.SectionHeader, inner);
                }
                else
                {
                    y += U(14);
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    AddSnapRow(s, ref y, rows[i], i == 0, i == rows.Count - 1);
                }
            }

            y += U(14);
            GUIStyle footer = new GUIStyle(_skin.Small) { alignment = TextAnchor.MiddleRight };
            AddSnapText(s, ref y, new GUIContent("BetterBepInEx  ·  " + DateTime.Now.ToString("yyyy-MM-dd")), footer, inner);
            y += s.Pad;

            SplitSnapshot(s, y);
        }

        private void AddSnapText(Snapshot s, ref float y, GUIContent content, GUIStyle style, float width)
        {
            float h = Mathf.Ceil(style.CalcHeight(content, width));
            s.Items.Add(new SnapItem { Kind = SnapItemKind.Text, Content = content, Style = style, Y = y, H = h });
            y += h;
        }

        private void AddSnapRow(Snapshot s, ref float y, Row row, bool first, bool last)
        {
            GUIStyle card = CardStyle(first, last);
            float textWidth = SnapshotTextWidth(s, card);
            float nameH = Mathf.Ceil(_skin.Label.CalcHeight(row.NamePlain, textWidth));
            GUIContent def = null;
            float defH = 0f;
            if (row.IsChanged)
            {
                def = new GUIContent("Default: " + DefaultText(row));
                defH = Mathf.Ceil(_skin.Small.CalcHeight(def, textWidth));
            }

            GUIContent value = new GUIContent(SnapshotValueText(row));
            float valueH;
            switch (row.Kind)
            {
                case RowKind.Bool:
                    valueH = U(26);
                    break;
                case RowKind.Range:
                case RowKind.Number:
                    valueH = Mathf.Ceil(s.NumberStyle.CalcHeight(value, Mathf.Min(U(140), s.ValueWidth)));
                    break;
                case RowKind.Color:
                    valueH = Mathf.Ceil(s.ValueStyle.CalcHeight(value, s.ValueWidth));
                    break;
                default:
                    valueH = Mathf.Ceil(s.ValueStyle.CalcHeight(value, s.ValueWidth));
                    break;
            }

            float h = card.padding.vertical + Mathf.Max(nameH + defH, valueH);
            s.Items.Add(new SnapItem
            {
                Kind = SnapItemKind.Row, Row = row, Content = value, Default = def,
                First = first, Last = last, Y = y, H = h, NameH = nameH, ValueH = valueH,
            });
            y += h;
        }

        private static string SnapshotValueText(Row row)
        {
            object value = row.Setting.Get();
            switch (row.Kind)
            {
                case RowKind.Shortcut:
                case RowKind.KeyCode:
                    return ShortcutText(value);
                case RowKind.Enum:
                case RowKind.List:
                    return SettingsModel.OptionText(value);
                case RowKind.Color:
                    return value is Color c ? "#" + ColorUtility.ToHtmlStringRGBA(c) : FormatValue(row, value);
                default:
                    string text = FormatValue(row, value);
                    return text.Length == 0 ? "(empty)" : text;
            }
        }

        /// <summary>
        /// Cuts a picture taller than <see cref="SnapshotMaxPart"/> into parts between rows. A cut
        /// row's card is closed above the cut and opened below it, and every part keeps a margin.
        /// </summary>
        private void SplitSnapshot(Snapshot s, float height)
        {
            int max = Mathf.Min(SnapshotMaxPart, SystemInfo.maxTextureSize);
            float partTop = 0f;
            int part = 0;
            for (int i = 0; i < s.Items.Count; i++)
            {
                SnapItem item = s.Items[i];
                if (i > 0 && item.Y + item.H + s.Pad - partTop > max)
                {
                    // Each part draws only its own items, so its bottom margin is empty.
                    s.Parts.Add(new Vector2(partTop, item.Y + s.Pad));
                    part++;
                    partTop = item.Y - s.Pad;
                    SnapItem before = s.Items[i - 1];
                    if (before.Kind == SnapItemKind.Row && item.Kind == SnapItemKind.Row)
                    {
                        before.Last = true;
                        s.Items[i - 1] = before;
                        item.First = true;
                    }
                }
                item.Part = part;
                s.Items[i] = item;
            }
            s.Parts.Add(new Vector2(partTop, Mathf.Ceil(height)));
        }
    }
}
