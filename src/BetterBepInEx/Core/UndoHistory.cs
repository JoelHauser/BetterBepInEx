using System;
using System.Collections.Generic;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// Undo and redo for setting changes made through the menu. No game or Unity dependency, so the
    /// tests compile it directly.
    ///
    /// A step is one or more changes that undo together. Pasting settings or resetting a mod is one
    /// group step. Dragging a slider fires a change on every frame, so a change to the same key
    /// within <see cref="MergeWindowSeconds"/> of the last one extends that step instead of adding a
    /// new one; otherwise one drag would take fifty presses of Ctrl+Z to walk back.
    /// </summary>
    public sealed class UndoHistory<TKey> where TKey : class
    {
        public const double MergeWindowSeconds = 1.0;
        public const int MaxSteps = 200;

        public sealed class Change
        {
            public TKey Key;
            public object OldValue;
            public object NewValue;
        }

        private sealed class Step
        {
            public string Label;
            public readonly List<Change> Changes = new List<Change>();
            public double LastTime;
            public bool IsGroup;
        }

        private readonly List<Step> _undo = new List<Step>();
        private readonly List<Step> _redo = new List<Step>();
        private Step _openGroup;

        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public bool CanUndo => _undo.Count > 0 && _openGroup == null;
        public bool CanRedo => _redo.Count > 0 && _openGroup == null;

        /// <summary>The label of the step Undo would revert, or null.</summary>
        public string NextUndoLabel => _undo.Count == 0 ? null : _undo[_undo.Count - 1].Label;

        public string NextRedoLabel => _redo.Count == 0 ? null : _redo[_redo.Count - 1].Label;

        public void Record(TKey key, object oldValue, object newValue, double time, string label)
        {
            if (key == null || Equals(oldValue, newValue))
            {
                return;
            }

            _redo.Clear();

            if (_openGroup != null)
            {
                Change existing = Find(_openGroup, key);
                if (existing != null)
                {
                    existing.NewValue = newValue;
                }
                else
                {
                    _openGroup.Changes.Add(new Change { Key = key, OldValue = oldValue, NewValue = newValue });
                }
                return;
            }

            Step last = _undo.Count == 0 ? null : _undo[_undo.Count - 1];
            if (last != null && !last.IsGroup && last.Changes.Count == 1 && last.Changes[0].Key == key
                && time - last.LastTime <= MergeWindowSeconds)
            {
                last.Changes[0].NewValue = newValue;
                last.LastTime = time;
                if (Equals(last.Changes[0].OldValue, newValue))
                {
                    // Dragged back to where it started: nothing left to undo.
                    _undo.RemoveAt(_undo.Count - 1);
                }
                return;
            }

            Step step = new Step { Label = label, LastTime = time };
            step.Changes.Add(new Change { Key = key, OldValue = oldValue, NewValue = newValue });
            Push(step);
        }

        /// <summary>
        /// Starts a group. Every change recorded until <see cref="EndGroup"/> undoes as one step.
        /// Groups do not nest; a second Begin while one is open throws, because that is a bug in the
        /// caller and silently merging two operations would make undo lie about what it reverts.
        /// </summary>
        public void BeginGroup(string label)
        {
            if (_openGroup != null)
            {
                throw new InvalidOperationException("An undo group is already open: " + _openGroup.Label);
            }
            _openGroup = new Step { Label = label, IsGroup = true };
        }

        public void EndGroup()
        {
            if (_openGroup == null)
            {
                return;
            }
            Step group = _openGroup;
            _openGroup = null;
            group.Changes.RemoveAll(c => Equals(c.OldValue, c.NewValue));
            if (group.Changes.Count > 0)
            {
                Push(group);
            }
        }

        /// <summary>
        /// Pops the last step. Returns the values to write back, newest change first, or null when
        /// there is nothing to undo. The caller writes them with recording suppressed.
        /// </summary>
        public List<KeyValuePair<TKey, object>> Undo(out string label)
        {
            label = null;
            if (!CanUndo)
            {
                return null;
            }
            Step step = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(step);
            label = step.Label;

            List<KeyValuePair<TKey, object>> writes = new List<KeyValuePair<TKey, object>>(step.Changes.Count);
            for (int i = step.Changes.Count - 1; i >= 0; i--)
            {
                writes.Add(new KeyValuePair<TKey, object>(step.Changes[i].Key, step.Changes[i].OldValue));
            }
            return writes;
        }

        public List<KeyValuePair<TKey, object>> Redo(out string label)
        {
            label = null;
            if (!CanRedo)
            {
                return null;
            }
            Step step = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(step);
            label = step.Label;

            List<KeyValuePair<TKey, object>> writes = new List<KeyValuePair<TKey, object>>(step.Changes.Count);
            foreach (Change change in step.Changes)
            {
                writes.Add(new KeyValuePair<TKey, object>(change.Key, change.NewValue));
            }
            return writes;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _openGroup = null;
        }

        private void Push(Step step)
        {
            _undo.Add(step);
            if (_undo.Count > MaxSteps)
            {
                _undo.RemoveAt(0);
            }
        }

        private static Change Find(Step step, TKey key)
        {
            foreach (Change change in step.Changes)
            {
                if (change.Key == key)
                {
                    return change;
                }
            }
            return null;
        }
    }
}
