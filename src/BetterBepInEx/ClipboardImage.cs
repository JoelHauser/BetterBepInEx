using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace BetterBepInEx
{
    /// <summary>
    /// Puts a picture on the Windows clipboard, so it pastes straight into Discord or anywhere else.
    /// Unity has no API for this. The image goes on as a bitmap (CF_DIB), which every program
    /// reads, and as "PNG", which browsers and Discord prefer.
    /// </summary>
    internal static class ClipboardImage
    {
        private const uint CfDib = 8;
        private const uint GmemMoveable = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);

        /// <summary>True when the picture is on the clipboard. Never throws.</summary>
        public static bool Copy(Texture2D texture, byte[] png)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer)
            {
                return false;
            }
            try
            {
                byte[] dib = Dib(texture);
                // With no owner window, EmptyClipboard leaves the clipboard ownerless and
                // SetClipboardData may then fail: own it with the game's window.
                IntPtr owner = GetActiveWindow();
                bool open = false;
                // Another program may hold the clipboard for a moment.
                for (int i = 0; i < 5 && !(open = OpenClipboard(owner)); i++)
                {
                    Thread.Sleep(10);
                }
                if (!open)
                {
                    return false;
                }
                try
                {
                    if (!EmptyClipboard() || !Put(CfDib, dib))
                    {
                        return false;
                    }
                    uint pngFormat = RegisterClipboardFormat("PNG");
                    if (pngFormat != 0)
                    {
                        Put(pngFormat, png);
                    }
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception e)
            {
                BetterBepInExPlugin.Log.LogWarning("Could not copy the screenshot to the clipboard: " + e.Message);
                return false;
            }
        }

        private static bool Put(uint format, byte[] data)
        {
            IntPtr memory = GlobalAlloc(GmemMoveable, (UIntPtr)data.Length);
            if (memory == IntPtr.Zero)
            {
                return false;
            }
            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero)
            {
                GlobalFree(memory);
                return false;
            }
            Marshal.Copy(data, 0, target, data.Length);
            GlobalUnlock(memory);
            if (SetClipboardData(format, memory) == IntPtr.Zero)
            {
                GlobalFree(memory);
                return false;
            }
            // The clipboard owns the memory now.
            return true;
        }

        /// <summary>
        /// A 32-bit bottom-up DIB: a BITMAPINFOHEADER, then BGRA pixels. Unity's rows already run
        /// bottom to top, as a DIB with a positive height expects.
        /// </summary>
        private static byte[] Dib(Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            byte[] dib = new byte[40 + pixels.Length * 4];
            WriteInt(dib, 0, 40);
            WriteInt(dib, 4, width);
            WriteInt(dib, 8, height);
            dib[12] = 1;
            dib[14] = 32;
            WriteInt(dib, 20, pixels.Length * 4);
            int o = 40;
            foreach (Color32 p in pixels)
            {
                dib[o++] = p.b;
                dib[o++] = p.g;
                dib[o++] = p.r;
                dib[o++] = 255;
            }
            return dib;
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
