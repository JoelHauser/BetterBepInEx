using System;

namespace BetterBepInEx.Core
{
    /// <summary>
    /// How big the menu is drawn for a given resolution. The layout is designed at 1920x1080.
    ///
    /// It scales with the screen's height, which keeps text the same physical size on 16:9, 21:9
    /// and 32:9 screens of the same height (an ultrawide is a wider 1440p, not a bigger one). On
    /// screens narrower than 16:9 (16:10, 4:3, a portrait monitor) it scales with the width
    /// instead, so the window still fits across.
    /// </summary>
    public static class ScreenScale
    {
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const float Min = 0.5f;
        public const float Max = 4f;

        public static float For(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return 1f;
            }
            float scale = Math.Min(height / DesignHeight, width / DesignWidth);
            return Math.Max(Min, Math.Min(Max, scale));
        }

        /// <summary>The resolution scale times the player's own "Menu scale" setting, kept in range.</summary>
        public static float Effective(int width, int height, float userScale, bool followResolution)
        {
            float scale = userScale * (followResolution ? For(width, height) : 1f);
            return Math.Max(Min, Math.Min(Max, scale));
        }
    }
}
