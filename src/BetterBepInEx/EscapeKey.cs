using System.Runtime.InteropServices;

namespace BetterBepInEx
{
    /// <summary>
    /// Esc, read from the keyboard itself. Not from IMGUI: its key events go to the focused window,
    /// so Esc worked only after clicking in the right place. Not from Unity's Input either: the
    /// menu calls Input.ResetInputAxes every frame, so it reports every key up.
    /// </summary>
    internal static class EscapeKey
    {
        private const int VkEscape = 0x1B;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        public static bool IsDown()
        {
            return (GetAsyncKeyState(VkEscape) & 0x8000) != 0;
        }
    }
}
