using System.Collections.Generic;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// Which blocks of a long scrolling list (headings and setting rows) to lay out. IMGUI lays out
    /// every control on every event, so only the blocks on screen are drawn; the rest are two
    /// spaces, one above and one below, of their total height.
    ///
    /// A block's height is known once it has been drawn and measured; until then it is estimated.
    /// Blocks drawn in the list take their real height at once, so an estimate that is wrong only
    /// moves what comes after it. That is harmless below the top of the view, but above it every
    /// visible row would jump. So blocks above the view are never drawn in the list until they are
    /// measured: they are drawn out of sight first (<see cref="Offscreen"/>). The menu keeps the
    /// view still across such corrections by remembering which row is at the top of the view and
    /// how far into it (<see cref="IndexAt"/>, <see cref="DisplayedTop"/>), and scrolling back to
    /// that row once the list is rebuilt with the new heights.
    /// </summary>
    public sealed class VirtualList
    {
        private float[] _y = new float[256];
        private float[] _h = new float[256];
        private bool[] _known = new bool[256];
        private float[] _shownTop = new float[256];
        private float[] _shownHeight = new float[256];

        public int Count { get; private set; }

        /// <summary>Height of every block together.</summary>
        public float Total { get; private set; }

        /// <summary>The first block drawn in the list: the first one reaching into the view.</summary>
        public int First { get; private set; }

        /// <summary>One past the last block drawn in the list.</summary>
        public int End { get; private set; }

        /// <summary>Blocks to draw out of sight this frame, only to measure them.</summary>
        public readonly List<int> Offscreen = new List<int>();

        public float TopSpace => First < Count ? _y[First] : Total;

        public float BottomSpace => End > First ? Total - (_y[End - 1] + _h[End - 1]) : Total - TopSpace;

        public float Y(int i) => _y[i];

        public float Height(int i) => _h[i];

        public bool Known(int i) => _known[i];

        public void Clear()
        {
            Count = 0;
            Total = 0f;
            First = 0;
            End = 0;
            Offscreen.Clear();
        }

        /// <param name="height">The measured height, or an estimate when <paramref name="known"/> is false.</param>
        public void Add(float height, bool known)
        {
            if (Count == _y.Length)
            {
                int size = Count * 2;
                System.Array.Resize(ref _y, size);
                System.Array.Resize(ref _h, size);
                System.Array.Resize(ref _known, size);
                System.Array.Resize(ref _shownTop, size);
                System.Array.Resize(ref _shownHeight, size);
            }
            _y[Count] = Total;
            _h[Count] = height;
            _known[Count] = known;
            Total += height;
            Count++;
        }

        /// <summary>
        /// Picks the blocks to draw for a view of <paramref name="view"/> pixels scrolled to
        /// <paramref name="scroll"/>.
        /// </summary>
        /// <param name="below">How far past the bottom of the view to keep drawing.</param>
        /// <param name="above">How far above the view unmeasured blocks are measured, ahead of scrolling up to them.</param>
        /// <param name="minHeight">
        /// The least an unmeasured block may turn out to be. The list reaches far enough below that,
        /// were every unmeasured block this short, the view would still be full.
        /// </param>
        /// <param name="spare">How many further unmeasured blocks to measure out of sight, nearest first.</param>
        public void Arrange(float scroll, float view, float below, float above, float minHeight, int spare)
        {
            Offscreen.Clear();

            // The first block whose bottom is below the top of the view. Bottoms never decrease.
            int lo = 0;
            int hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_y[mid] + _h[mid] > scroll)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            First = lo;

            float limit = scroll + view + below;
            float y = First < Count ? _y[First] : Total;
            int end = First;
            while (end < Count && y < limit)
            {
                y += _known[end] ? _h[end] : System.Math.Min(_h[end], minHeight);
                end++;
            }
            End = end;

            // Until the list is drawn, it is shown where the estimates put it.
            for (int i = First; i < End; i++)
            {
                _shownTop[i] = _y[i];
                _shownHeight[i] = _h[i];
            }

            int top = First;
            for (int i = First - 1; i >= 0 && _y[i] + _h[i] > scroll - above; i--)
            {
                top = i;
                if (!_known[i])
                {
                    Offscreen.Add(i);
                }
            }

            for (int i = End; i < Count && spare > 0; i++)
            {
                if (!_known[i])
                {
                    Offscreen.Add(i);
                    spare--;
                }
            }
            for (int i = top - 1; i >= 0 && spare > 0; i--)
            {
                if (!_known[i])
                {
                    Offscreen.Add(i);
                    spare--;
                }
            }
        }

        /// <summary>Where a block drawn in the list (First to End) actually landed, once drawn.</summary>
        public void Shown(int i, float top, float height)
        {
            if (i >= First && i < End)
            {
                _shownTop[i] = top;
                _shownHeight[i] = height;
            }
        }

        /// <summary>
        /// Where a block is on screen, in the list's own coordinates: the space above puts blocks
        /// before <see cref="First"/> where the arrangement did, and blocks after <see cref="End"/>
        /// move with whatever the drawn blocks really measured.
        /// </summary>
        public float DisplayedTop(int i)
        {
            if (i < First)
            {
                return _y[i];
            }
            if (i < End)
            {
                return _shownTop[i];
            }
            if (End <= First)
            {
                return _y[i];
            }
            float drawnBottom = _shownTop[End - 1] + _shownHeight[End - 1];
            return _y[i] + drawnBottom - (_y[End - 1] + _h[End - 1]);
        }

        /// <summary>The block shown at <paramref name="y"/>: the last one starting at or above it. -1 when empty.</summary>
        public int IndexAt(float y)
        {
            if (Count == 0)
            {
                return -1;
            }
            int lo = 0;
            int hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (DisplayedTop(mid) <= y)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return lo;
        }
    }
}
