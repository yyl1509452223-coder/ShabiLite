using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShabiLite.Services
{
    internal sealed class WallpaperItem
    {
        public string FileName { get; set; }
        public string Path { get; set; }
        public string PreviewPath { get; set; }
        public ImageSource Thumbnail { get; set; }
    }

    internal static class VideoThumbnailProvider
    {
        public static ImageSource GetThumbnail(string path, int width, int height)
        {
            var previewPath = FindPreviewPath(path);
            if (previewPath != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = width;
                    bitmap.UriSource = new Uri(previewPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch
                {
                }
            }

            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                var factoryGuid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryGuid, out factory);
                var result = factory.GetImage(
                    new NativeSize { Width = width, Height = height },
                    ShellImageFlags.BiggerSizeOk | ShellImageFlags.ThumbnailOnly,
                    out bitmapHandle);
                if (result != 0 || bitmapHandle == IntPtr.Zero)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(width, height));
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
            }
        }

        public static string FindPreviewPath(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                return null;
            }

            foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".gif" })
            {
                var candidate = Path.ChangeExtension(videoPath, extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        [Flags]
        private enum ShellImageFlags
        {
            BiggerSizeOk = 0x1,
            ThumbnailOnly = 0x8
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, ShellImageFlags flags, out IntPtr bitmapHandle);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string path,
            IntPtr bindingContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr objectHandle);
    }
}
