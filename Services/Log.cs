using System;
using System.IO;
using System.Text;

namespace ShabiLite.Services
{
    internal static class Log
    {
        private static readonly object SyncRoot = new object();
        private static readonly string DirectoryPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "鲨壁");

        public static string Path
        {
            get { return System.IO.Path.Combine(DirectoryPath, "shabi.log"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(
                        Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never interrupt wallpaper playback.
            }
        }
    }
}
