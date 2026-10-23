using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace BetterBepInEx
{
    internal sealed partial class MenuWindow
    {
        private sealed class PopupState
        {
            public Row Row;
            public Rect Rect;
            public GUIContent[] Labels;
            public int Selected;
            public Action<int> OnPick;
            public Vector2 Scroll;
            public bool Focused;
        }

        private static readonly GUIContent ArrowLabel = new GUIContent("▼");
        private static readonly GUIContent CaptureLabel = new GUIContent("Press a key or combination...   Esc cancels");
        private static readonly GUIContent NotBoundLabel = new GUIContent("Not bound");
        private static readonly GUIContent PlusLabel = new GUIContent("+");
        private static readonly GUIContent UnbindLabel = new GUIContent("×", "Unbind");
        private static readonly GUIContent KeyTip = new GUIContent(string.Empty, "Click, then press the key or combination to bind");

        private static readonly KeyCode[] ModifierKeys =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand, KeyCode.AltGr,
        };

        private PopupState _popup;

        private Row _editingRow;
        private string _editBuffer;

        private Row _capturing;
        private float _captureStarted;
        private bool _hotkeyOverrideBefore;
        private readonly List<KeyCode> _capturePressed = new List<KeyCode>();
        private KeyCode[] _keysToCheck;

        private readonly HashSet<Row> _expandedColors = new HashSet<Row>();
        private readonly Dictionary<object, float> _switchAnim = new Dictionary<object, float>();
        private Row _colorEditRow;
        private string _colorEditBuffer;

        private bool IsCapturing => _capturing != null;

        private void DrawValue(Row row)
        {
            switch (row.Kind)
            {
                case RowKind.Bool:
                    DrawSwitch(row);
                    break;
                case RowKind.Range:
                    DrawRange(row);
                    break;
                case RowKind.Number:
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    DrawEditField(row, _skin.NumberField, W(Mathf.Min(U(140), _valueWidth)));
                    GUILayout.EndHorizontal();
                    break;
                case RowKind.Text:
                case RowKind.Converted:
                    DrawEditField(row, _skin.TextField, Expand);
                    break;
                case RowKind.Enum:
                case RowKind.List:
                    DrawDropdown(row);
                    break;
                case RowKind.Flags:
                    DrawFlags(row);
                    break;
                case RowKind.Shortcut:
                case RowKind.KeyCode:
                    DrawKeybind(row);
                    break;
                case RowKind.Color:
                    DrawColor(row);
                    break;
                default:
                    GUILayout.BeginHorizontal();
                    _stock.DrawValue(row.Setting);
                    GUILayout.EndHorizontal();
                    if (row.Entry == null)
                    {
                        _model.Touch(row);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ switch

        /// <summary>A pill switch, right-aligned, whose knob slides over about a tenth of a second.</summary>
        private void DrawSwitch(Row row)
        {
            bool value = row.Setting.Get() is bool b && b;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect r = GUILayoutUtility.GetRect(U(46), U(26), Opts(Opt.Size, U(46), U(26)));
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                _model.Write(row, !value);
            }
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Repaint)
            {
                DrawPill(r, value, row, r.Contains(Event.current.mousePosition) && GUI.enabled);
            }
        }

        /// <summary>The pill switch's look, on Repaint: track, accent fill fading in, and the sliding knob.</summary>
        private void DrawPill(Rect r, bool value, object key, bool hover)
        {
            float target = value ? 1f : 0f;
            if (!_switchAnim.TryGetValue(key, out float t))
            {
                t = target;
            }
            t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime * 9f);
            _switchAnim[key] = t;

            _skin.TrackOff.Draw(r, false, false, false, false);
            if (t > 0f)
            {
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, t * (GUI.enabled ? 1f : 0.4f) * old.a);
                _skin.TrackOn.Draw(r, false, false, false, false);
                GUI.color = old;
            }
            float inset = Mathf.Max(2f, Mathf.Round(r.height * 0.12f));
            float knob = r.height - inset * 2f;
            float x = r.x + inset + t * (r.width - knob - inset * 2f);
            Color knobColor = Color.Lerp(new Color(0.8f, 0.82f, 0.85f), Color.white, t);
            if (hover)
            {
                knobColor = Color.white;
            }
            _skin.Fill(new Rect(x, r.y + inset, knob, knob), knobColor, _skin.KnobTex);
        }

        // ------------------------------------------------------------------ numbers

        /// <summary>
        /// A slider with the part left of the thumb filled in the accent, and the value in a box
        /// beside it. The track and fill are drawn here; GUI.HorizontalSlider only moves the thumb.
        /// </summary>
        private void DrawRange(Row row)
        {
            Type type = row.Setting.SettingType;
            object value = row.Setting.Get();
            float min = ToFloat(row.Setting.AcceptableValueRange.Key);
            float max = ToFloat(row.Setting.AcceptableValueRange.Value);
            float current = ToFloat(value);

            GUILayout.BeginHorizontal();
            Rect r = GUILayoutUtility.GetRect(U(80), U(30), _skin.Slider, Expand);
            float thumb = _skin.SliderThumb.fixedWidth;
            float left = r.x + thumb / 2f;
            float width = Mathf.Max(1f, r.width - thumb);
            bool sliding = _slideRow == row;

            // Handled here rather than by GUI.HorizontalSlider: a press anywhere on the track takes
            // the thumb there and keeps hold of it, and the thumb follows the mouse exactly while
            // the setting gets the rounded value. The drag is held by row as well as by control id,
            // so nothing appearing above it mid-drag (the Reset link) can make it let go.
            int id = GUIUtility.GetControlID(SliderHint, FocusType.Passive, r);
            Event e = Event.current;
            switch (sliding ? e.type : e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && GUI.enabled && max > min && r.Contains(e.mousePosition):
                    BeginSlide(row);
                    GUIUtility.hotControl = id;
                    sliding = true;
                    SlideTo(row, Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - left) / width)), min, max, type, value);
                    e.Use();
                    break;
                case EventType.MouseDrag when sliding:
                    GUIUtility.hotControl = id;
                    SlideTo(row, Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - left) / width)), min, max, type, value);
                    e.Use();
                    break;
                case EventType.MouseUp when sliding:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                    }
                    EndSlide();
                    sliding = false;
                    e.Use();
                    break;
            }

            if (e.type == EventType.Repaint)
            {
                float shown = sliding ? _slideValue : current;
                float frac = max > min ? Mathf.Clamp01((shown - min) / (max - min)) : 0f;
                float trackHeight = U(6);
                Rect track = new Rect(left, r.y + (r.height - trackHeight) / 2f, width, trackHeight);
                Color old = GUI.color;
                if (!GUI.enabled)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f * old.a);
                }
                _skin.SliderTrack.Draw(track, false, false, false, false);
                if (frac > 0f)
                {
                    _skin.SliderFill.Draw(new Rect(track.x, track.y, Mathf.Max(trackHeight, width * frac), trackHeight), false, false, false, false);
                }
                float knob = r.height - _skin.Slider.padding.vertical;
                Rect thumbRect = new Rect(Mathf.Round(left + width * frac - thumb / 2f), r.y + _skin.Slider.padding.top, thumb, knob);
                bool hover = GUI.enabled && !ListMoving && r.Contains(e.mousePosition);
                _skin.SliderThumb.Draw(thumbRect, hover || sliding, sliding, false, false);
                GUI.color = old;
            }
            if (row.Setting.ShowRangeAsPercent == true)
            {
                float percent = max > min ? 100f * ((_slideRow == row ? _slideValue : current) - min) / (max - min) : 0f;
                GUILayout.Label(CountedLabel(string.Empty, Mathf.RoundToInt(percent), 40, "%"), _skin.Label, W(U(56)));
            }
            else
            {
                DrawEditField(row, _skin.NumberField, W(U(76)));
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Rounds a slider position to a sensible step: whole numbers for integer types, and about
        /// a hundredth of the range otherwise, so dragging 0..1 gives 0.37 rather than 0.3714286.
        /// </summary>
        internal static float Step(float value, float min, float max, Type type)
        {
            if (SettingsModel.IsWholeNumber(type))
            {
                return Mathf.Round(value);
            }
            float range = Mathf.Abs(max - min);
            if (range <= 0f)
            {
                return value;
            }
            float step = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(range)) - 2f);
            float rounded = Mathf.Round(value / step) * step;
            return Mathf.Clamp((float)Math.Round(rounded, 6), Mathf.Min(min, max), Mathf.Max(min, max));
        }

        private static readonly int SliderHint = "BetterBepInEx.Slider".GetHashCode();

        /// <summary>The row whose slider is held, and where its thumb is: the mouse's value, not yet rounded.</summary>
        private Row _slideRow;
        private float _slideValue;
        private object _slideWritten;

        /// <summary>The rounded value the thumb is on, not yet given to the setting; and when the thumb last moved.</summary>
        private object _slidePending;
        private float _slideMovedAt;

        /// <summary>How long the thumb has to rest before the setting follows it mid-drag.</summary>
        private const float SlideApplyAfter = 0.15f;
        private bool _slideShowReset;
        private ConfigFile _slideFile;
        private bool _slideFileSaved;

        private void BeginSlide(Row row)
        {
            EndSlide();
            CommitEdit();
            ClosePopup();
            _slideRow = row;
            _slideWritten = null;
            _slidePending = null;
            _slideShowReset = row.IsChanged && row.CanReset;
            // The mod's .cfg is written once, on release, not on every step of the drag: BepInEx
            // saves the whole file on each change, which stutters a drag.
            _slideFile = row.Entry?.ConfigFile;
            if (_slideFile != null)
            {
                _slideFileSaved = _slideFile.SaveOnConfigSet;
                _slideFile.SaveOnConfigSet = false;
            }
            // The whole drag is one undo step.
            _model.History.BeginGroup(row.Name.text);
        }

        /// <summary>
        /// Moves the thumb to <paramref name="raw"/>. The setting is not changed on every step: a
        /// change runs the mod's own handlers, and some rebuild things on each one, which made the
        /// game hitch on every step and the thumb lag the mouse. It follows when the thumb rests
        /// for a moment (<see cref="ApplyRestingSlide"/>, from Update) and on release.
        /// </summary>
        private void SlideTo(Row row, float raw, float min, float max, Type type, object value)
        {
            _slideValue = raw;
            _slideMovedAt = Time.realtimeSinceStartup;
            object converted = FromFloat(Step(raw, min, max, type), type);
            if (converted == null)
            {
                return;
            }
            if (_editingRow == row)
            {
                _editingRow = null;
            }
            _slidePending = Equals(converted, _slideWritten ?? value) ? null : converted;
        }

        /// <summary>Gives the setting the thumb's value once the thumb has rested, so a pause in a drag previews it.</summary>
        private void ApplyRestingSlide()
        {
            if (_slideRow != null && _slidePending != null && Time.realtimeSinceStartup - _slideMovedAt >= SlideApplyAfter)
            {
                ApplySlide();
            }
        }

        private void ApplySlide()
        {
            object value = _slidePending;
            _slidePending = null;
            if (value == null || Equals(value, _slideWritten))
            {
                return;
            }
            // Recorded into the drag's undo group, also when called from Update.
            _model.RecordDepth++;
            try
            {
                _model.Write(_slideRow, value);
            }
            finally
            {
                _model.RecordDepth--;
            }
            _slideWritten = value;
        }

        /// <summary>The number shown beside a slider: where the thumb is while it is held.</summary>
        private string SlideText(Row row)
        {
            return _slidePending != null ? FormatValue(row, _slidePending) : ShownValue(row);
        }

        private void EndSlide()
        {
            if (_slideRow == null)
            {
                return;
            }
            ApplySlide();
            _slideRow = null;
            _model.History.EndGroup();
            ConfigFile file = _slideFile;
            _slideFile = null;
            if (file == null)
            {
                return;
            }
            file.SaveOnConfigSet = _slideFileSaved;
            if (_slideWritten != null && _slideFileSaved)
            {
                try
                {
                    file.Save();
                }
                catch (Exception e)
                {
                    BetterBepInExPlugin.Log.LogWarning("Could not save " + file.ConfigFilePath + ": " + e.Message);
                }
            }
        }

        private static float ToFloat(object value)
        {
            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private static object FromFloat(float value, Type type)
        {
            try
            {
                return Convert.ChangeType(SettingsModel.IsWholeNumber(type) ? Math.Round(value) : (double)value, type, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ edit fields

        /// <summary>
        /// A text field that edits a buffer and writes the setting when you press Enter or click
        /// away. The stock menu writes on every keystroke, which saves the mod's .cfg on every
        /// keystroke and fights you when typing "0." into a float.
        /// </summary>
        private void DrawEditField(Row row, GUIStyle style, params GUILayoutOption[] options)
        {
            bool focused = _focusedName == row.ControlName;
            string shown = _editingRow == row ? _editBuffer : _slideRow == row ? SlideText(row) : ShownValue(row);
            GUI.SetNextControlName(row.ControlName);
            string typed = GUILayout.TextField(shown ?? string.Empty, style, options);
            if (typed != shown)
            {
                if (_editingRow != null && _editingRow != row)
                {
                    CommitEdit();
                }
                _editingRow = row;
                _editBuffer = typed;
            }
            else if (_editingRow == row && !focused && Event.current.type == EventType.Layout)
            {
                // On Layout, so the Repaint that follows lays out the same controls the commit
                // produced (a Reset link appearing, say).
                CommitEdit();
            }
        }

        /// <summary>The value's text for the row's field, formatted again only when the value changes.</summary>
        private static string ShownValue(Row row)
        {
            object value = row.Setting.Get();
            if (!(value is ValueType) && !(value is string))
            {
                // A mod's own type may change inside without becoming a different object.
                return FormatValue(row, value);
            }
            if (row.ValueText == null || !Equals(value, row.ValueTextOf))
            {
                row.ValueText = FormatValue(row, value);
                row.ValueTextOf = value;
            }
            return row.ValueText;
        }

        private static string FormatValue(Row row) => FormatValue(row, row.Setting.Get());

        private static string FormatValue(Row row, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (row.Kind == RowKind.Converted && row.Setting.ObjToStr != null)
            {
                try
                {
                    return row.Setting.ObjToStr(value);
                }
                catch (Exception)
                {
                    return value.ToString();
                }
            }
            if (value is float f)
            {
                return f.ToString("0.#######", CultureInfo.InvariantCulture);
            }
            if (value is double d)
            {
                return d.ToString("0.##########", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Writes the pending text edit, if there is one. Safe to call at any time.</summary>
        private void CommitEdit()
        {
            Row row = _editingRow;
            string text = _editBuffer;
            _editingRow = null;
            _editBuffer = null;
            if (row == null || text == null || text == FormatValue(row))
            {
                return;
            }

            object parsed;
            Type type = row.Setting.SettingType;
            try
            {
                if (row.Kind == RowKind.Text)
                {
                    parsed = text;
                }
                else if (row.Kind == RowKind.Converted)
                {
                    parsed = row.Setting.StrToObj(text);
                }
                else
                {
                    double number = double.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (row.Kind == RowKind.Range)
                    {
                        double min = Convert.ToDouble(row.Setting.AcceptableValueRange.Key, CultureInfo.InvariantCulture);
                        double max = Convert.ToDouble(row.Setting.AcceptableValueRange.Value, CultureInfo.InvariantCulture);
                        number = Math.Max(Math.Min(min, max), Math.Min(Math.Max(min, max), number));
                    }
                    if (SettingsModel.IsWholeNumber(type))
                    {
                        number = Math.Round(number);
                    }
                    parsed = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                SetStatus("\"" + text + "\" is not a valid value for " + row.Name.text + ".", true);
                return;
            }

            _model.RecordDepth++;
            try
            {
                _model.Write(row, parsed);
            }
            finally
            {
                _model.RecordDepth--;
            }
        }

        // ------------------------------------------------------------------ dropdown

        private void DrawDropdown(Row row)
        {
            object value = row.Setting.Get();
            int index = IndexOf(row.Options, value);
            GUIContent label = index >= 0 ? row.OptionLabels[index] : Cached(SettingsModel.OptionText(value));

            Rect r = GUILayoutUtility.GetRect(label, _skin.Dropdown, Expand);
            bool open = _popup != null && _popup.Row == row;
            if (GUI.Button(r, label, _skin.Dropdown))
            {
                if (open)
                {
                    ClosePopup();
                }
                else
                {
                    object[] options = row.Options;
                    OpenPopup(row, r, row.OptionLabels, index, i => _model.Write(row, options[i]));
                }
            }
            if (Event.current.type == EventType.Repaint)
            {
                _skin.KeyPlus.Draw(new Rect(r.xMax - U(26), r.y, U(18), r.height), ArrowLabel, false, false, false, false);
            }
        }

        private static int IndexOf(object[] options, object value)
        {
            if (options == null)
            {
                return -1;
            }
            for (int i = 0; i < options.Length; i++)
            {
                if (Equals(options[i], value))
                {
                    return i;
                }
            }
            return -1;
        }

        private void OpenPopup(Row row, Rect anchor, GUIContent[] labels, int selected, Action<int> onPick)
        {
            CommitEdit();
            Vector2 topLeft = GUIUtility.GUIToScreenPoint(new Vector2(anchor.x, anchor.y));
            float itemHeight = _skin.PopupItem.CalcHeight(labels.Length > 0 ? labels[0] : GUIContent.none, anchor.width);
            float height = Mathf.Min(labels.Length * itemHeight + U(8), U(380));
            float width = anchor.width;
            foreach (GUIContent l in labels)
            {
                width = Mathf.Max(width, _skin.PopupItem.CalcSize(l).x + U(24));
            }
            width = Mathf.Min(width, U(520));

            float y = topLeft.y + anchor.height + U(4);
            if (y + height > Screen.height)
            {
                y = Mathf.Max(0f, topLeft.y - height - U(4));
            }
            float x = Mathf.Min(topLeft.x, Screen.width - width);

            _popup = new PopupState
            {
                Row = row,
                Rect = new Rect(x, y, width, height),
                Labels = labels,
                Selected = selected,
                OnPick = onPick,
            };
            if (selected > 0)
            {
                _popup.Scroll = new Vector2(0f, Mathf.Max(0f, selected * itemHeight - height / 2f));
            }
        }

        private void ClosePopup()
        {
            _popup = null;
        }

        private void HandlePopupOutsideClick(Event e)
        {
            if (_popup != null && e.type == EventType.MouseDown && !_popup.Rect.Contains(e.mousePosition))
            {
                ClosePopup();
                e.Use();
            }
        }

        private void DrawPopup(int id)
        {
            PopupState popup = _popup;
            if (popup == null)
            {
                return;
            }
            if (!popup.Focused)
            {
                GUI.FocusWindow(PopupId);
                popup.Focused = true;
            }
            _model.RecordDepth++;
            try
            {
                GUILayout.BeginArea(new Rect(U(4), U(4), popup.Rect.width - U(8), popup.Rect.height - U(8)));
                popup.Scroll = GUILayout.BeginScrollView(popup.Scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
                for (int i = 0; i < popup.Labels.Length; i++)
                {
                    if (GUILayout.Button(popup.Labels[i], i == popup.Selected ? _skin.PopupItemActive : _skin.PopupItem, Expand))
                    {
                        ClosePopup();
                        popup.OnPick(i);
                        break;
                    }
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (ArgumentException)
            {
            }
            finally
            {
                _model.RecordDepth--;
            }
        }

        // ------------------------------------------------------------------ flags

        private void DrawFlags(Row row)
        {
            Type type = row.Setting.SettingType;
            long current = Convert.ToInt64(row.Setting.Get(), CultureInfo.InvariantCulture);
            if (row.Options == null)
            {
                row.Options = Enum.GetValues(type).Cast<object>().Where(v => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0).ToArray();
                row.OptionLabels = row.Options.Select(o => new GUIContent(SettingsModel.OptionText(o))).ToArray();
            }

            float used = 0f;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            for (int i = 0; i < row.Options.Length; i++)
            {
                long bit = Convert.ToInt64(row.Options[i], CultureInfo.InvariantCulture);
                bool on = (current & bit) == bit;
                GUIStyle style = on ? _skin.ChipOn : _skin.Chip;
                float w = style.CalcSize(row.OptionLabels[i]).x + U(4);
                if (used > 0f && used + w > _valueWidth)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    used = 0f;
                }
                used += w;
                if (GUILayout.Button(row.OptionLabels[i], style, NoExpand))
                {
                    long next = on ? current & ~bit : current | bit;
                    _model.Write(row, Enum.ToObject(type, next));
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ------------------------------------------------------------------ keybinds

        /// <summary>
        /// A field showing the bind as keycaps, Ctrl + H, with an unbind button beside it. The
        /// button's slot is kept even when there is nothing to unbind, so the fields line up.
        /// </summary>
        private void DrawKeybind(Row row)
        {
            object value = row.Setting.Get();
            bool capturing = _capturing == row;
            GUIContent[] caps = KeyCaps(row, value);

            GUILayout.BeginHorizontal();
            GUIStyle field = capturing ? _skin.KeyFieldActive : _skin.KeyField;
            Rect r = GUILayoutUtility.GetRect(U(120), field.fixedHeight, field, Expand);
            if (GUI.Button(r, KeyTip, field))
            {
                if (capturing)
                {
                    CancelCapture();
                }
                else
                {
                    StartCapture(row);
                }
            }
            if (caps.Length > 0 && !capturing && !row.IsMenuHotkey)
            {
                if (GUILayout.Button(UnbindLabel, _skin.IconButton))
                {
                    _model.Write(row, row.Kind == RowKind.Shortcut ? (object)KeyboardShortcut.Empty : KeyCode.None);
                }
            }
            else
            {
                GUILayout.Space(_skin.IconButton.fixedWidth + _skin.IconButton.margin.horizontal);
            }
            GUILayout.EndHorizontal();

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            Rect inner = new Rect(r.x + U(8), r.y, r.width - U(16), r.height);
            if (capturing)
            {
                _skin.KeyHintActive.Draw(inner, CaptureLabel, false, false, false, false);
                return;
            }
            if (caps.Length == 0)
            {
                _skin.KeyHint.Draw(inner, NotBoundLabel, false, false, false, false);
                return;
            }
            float x = inner.x;
            float h = _skin.Keycap.fixedHeight;
            float y = r.y + (r.height - h) / 2f;
            for (int i = 0; i < caps.Length; i++)
            {
                float w = _skin.Keycap.CalcSize(caps[i]).x;
                if (x + w > inner.xMax)
                {
                    break;
                }
                _skin.Keycap.Draw(new Rect(x, y, w, h), caps[i], false, false, false, false);
                x += w + U(4);
                if (i < caps.Length - 1)
                {
                    _skin.KeyPlus.Draw(new Rect(x, y, U(12), h), PlusLabel, false, false, false, false);
                    x += U(16);
                }
            }
        }

        /// <summary>The keycaps for a value, rebuilt only when the value changes.</summary>
        private static GUIContent[] KeyCaps(Row row, object value)
        {
            if (row.KeyCaps != null && Equals(row.KeyCapsValue, value))
            {
                return row.KeyCaps;
            }
            List<GUIContent> caps = new List<GUIContent>();
            if (value is KeyboardShortcut shortcut && shortcut.MainKey != KeyCode.None)
            {
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    caps.Add(new GUIContent(KeyName(modifier)));
                }
                caps.Add(new GUIContent(KeyName(shortcut.MainKey)));
            }
            else if (value is KeyCode key && key != KeyCode.None)
            {
                caps.Add(new GUIContent(KeyName(key)));
            }
            row.KeyCaps = caps.ToArray();
            row.KeyCapsValue = value;
            return row.KeyCaps;
        }

        private void StartCapture(Row row)
        {
            // Clicking a second keybind while one is listening: end the first properly, or the
            // hotkey override it saved would be saved again as "true" and F12 would stay ignored.
            CancelCapture();
            CommitEdit();
            ClosePopup();
            _capturing = row;
            _captureStarted = Time.realtimeSinceStartup;
            _capturePressed.Clear();
            // So pressing F12 to bind it does not also close the menu. Put back as found: a mod
            // may hold the override on for its own reasons.
            _hotkeyOverrideBefore = _stock.OverrideHotkey;
            _stock.OverrideHotkey = true;
            GUIUtility.keyboardControl = 0;
        }

        private void CancelCapture()
        {
            if (_capturing == null)
            {
                return;
            }
            _capturing = null;
            _capturePressed.Clear();
            _stock.OverrideHotkey = _hotkeyOverrideBefore;
        }

        /// <summary>
        /// Key capture runs from Update, where Unity's key state is reliable once per frame. Every
        /// key pressed while capturing is collected; the bind is made when the first of them is
        /// released. The main key is the last non-modifier pressed, the rest are modifiers. So
        /// Ctrl+H binds Ctrl+H whichever of the two you let go of first, which the stock capture
        /// gets wrong (letting go of Ctrl first binds Ctrl with H as the modifier).
        /// </summary>
        public void Update()
        {
            ApplyRestingSlide();
            if (_capturing == null)
            {
                return;
            }
            if (Time.realtimeSinceStartup - _captureStarted < 0.1f)
            {
                return;
            }

            var input = UnityInput.Current;
            if (_keysToCheck == null)
            {
                _keysToCheck = input.SupportedKeyCodes.Where(k => k != KeyCode.None && k != KeyCode.Mouse0).ToArray();
            }

            foreach (KeyCode key in _keysToCheck)
            {
                if (input.GetKeyDown(key) && !_capturePressed.Contains(key))
                {
                    _capturePressed.Add(key);
                }
            }

            KeyCode released = KeyCode.None;
            foreach (KeyCode key in _capturePressed)
            {
                if (input.GetKeyUp(key) || !input.GetKey(key))
                {
                    released = key;
                    break;
                }
            }
            if (released == KeyCode.None)
            {
                return;
            }

            Row row = _capturing;
            List<KeyCode> pressed = new List<KeyCode>(_capturePressed);
            CancelCapture();

            if (pressed.Count == 1 && pressed[0] == KeyCode.Escape)
            {
                SetStatus("Kept " + ShortcutText(row.Setting.Get()) + ".");
                return;
            }

            KeyCode main = pressed.LastOrDefault(k => Array.IndexOf(ModifierKeys, k) < 0);
            if (main == KeyCode.None)
            {
                main = pressed[pressed.Count - 1];
            }
            KeyCode[] modifiers = pressed.Where(k => k != main).ToArray();

            object value;
            if (row.Kind == RowKind.KeyCode)
            {
                if (row.Options != null && Array.IndexOf(row.Options, main) < 0)
                {
                    SetStatus(KeyName(main) + " is not one of the keys " + row.Name.text + " accepts.", true);
                    return;
                }
                value = main;
            }
            else
            {
                value = new KeyboardShortcut(main, modifiers);
            }

            _model.RecordDepth++;
            try
            {
                _model.Write(row, value);
            }
            finally
            {
                _model.RecordDepth--;
            }
            SetStatus(row.Name.text + " is now " + ShortcutText(value) + ".");
        }

        // ------------------------------------------------------------------ color

        private void DrawColor(Row row)
        {
            Color color = row.Setting.Get() is Color c ? c : Color.white;
            GUILayout.BeginHorizontal();
            Rect swatch = GUILayoutUtility.GetRect(U(40), U(30), Opts(Opt.Size, U(40), U(30)));
            if (Event.current.type == EventType.Repaint)
            {
                _skin.Fill(swatch, Skin.InputBorder);
                _skin.Fill(new Rect(swatch.x + 1, swatch.y + 1, swatch.width - 2, swatch.height - 2), new Color(color.r, color.g, color.b, 1f));
            }
            GUILayout.Space(U(6));

            if (row.ValueText == null || !(row.ValueTextOf is Color shownColor) || shownColor != color)
            {
                row.ValueText = "#" + ColorUtility.ToHtmlStringRGBA(color);
                row.ValueTextOf = color;
            }
            string hex = row.ValueText;
            bool focused = _focusedName == row.ControlName;
            string shown = focused && _colorEditRow == row ? _colorEditBuffer : hex;
            GUI.SetNextControlName(row.ControlName);
            string typed = GUILayout.TextField(shown, _skin.TextField, Expand);
            if (typed != shown)
            {
                _colorEditRow = row;
                _colorEditBuffer = typed;
                string candidate = typed.StartsWith("#", StringComparison.Ordinal) ? typed : "#" + typed;
                if ((candidate.Length == 7 || candidate.Length == 9) && ColorUtility.TryParseHtmlString(candidate, out Color parsed))
                {
                    _model.Write(row, parsed);
                }
            }
            else if (!focused && _colorEditRow == row)
            {
                _colorEditRow = null;
            }

            bool expanded = _expandedColors.Contains(row);
            if (GUILayout.Button(Cached(expanded ? "Done" : "RGBA"), expanded ? _skin.ChipOn : _skin.Chip, NoExpand))
            {
                if (!_expandedColors.Remove(row))
                {
                    _expandedColors.Add(row);
                }
            }
            GUILayout.EndHorizontal();

            if (expanded)
            {
                Color next = color;
                next.r = ChannelSlider("R", color.r);
                next.g = ChannelSlider("G", color.g);
                next.b = ChannelSlider("B", color.b);
                next.a = ChannelSlider("A", color.a);
                if (next != color)
                {
                    _model.Write(row, next);
                }
            }
        }

        private float ChannelSlider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Cached(label), _skin.Small, W(U(16)));
            Rect r = GUILayoutUtility.GetRect(U(60), U(30), _skin.Slider, Expand);
            if (Event.current.type == EventType.Repaint)
            {
                float thumb = _skin.SliderThumb.fixedWidth;
                Rect track = new Rect(r.x + thumb / 2f, r.y + (r.height - U(6)) / 2f, r.width - thumb, U(6));
                _skin.SliderTrack.Draw(track, false, false, false, false);
                _skin.SliderFill.Draw(new Rect(track.x, track.y, Mathf.Max(U(6), track.width * value), track.height), false, false, false, false);
            }
            float v = GUI.HorizontalSlider(r, value, 0f, 1f, _skin.Slider, _skin.SliderThumb);
            GUILayout.Label(CountLabel(Mathf.RoundToInt(v * 255f)), _skin.Small, W(U(32)));
            GUILayout.EndHorizontal();
            // Only snap to 1/255 when the slider moved; snapping an untouched 0.3 would write the
            // setting on every frame the sliders are open.
            return Mathf.Approximately(v, value) ? value : Mathf.Round(v * 255f) / 255f;
        }
    }
}
