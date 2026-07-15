using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ShabiLite.Interop
{
    internal static class WindowAppearance
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCaptionColor = 35;

        public static void ApplyDarkTitleBar(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, Marshal.SizeOf(typeof(int)));

            var round = 2;
            DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref round, Marshal.SizeOf(typeof(int)));

            var captionColor = ColorRef(42, 34, 66);
            DwmSetWindowAttribute(handle, DwmCaptionColor, ref captionColor, Marshal.SizeOf(typeof(int)));
        }

        private static int ColorRef(byte red, byte green, byte blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
    }
}
