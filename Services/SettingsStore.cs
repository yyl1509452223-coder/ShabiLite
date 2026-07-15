using System;
using System.IO;
using System.Text;

namespace ShabiLite.Services
{
    internal sealed class AppSettings
    {
        public string VideoPath { get; set; }
        public string ScaleMode { get; set; } = "UniformToFill";
        public bool IsMuted { get; set; } = true;
        public string SteamCmdPath { get; set; }
        public string SteamUserName { get; set; } = SteamCmdService.DefaultSteamUserName;
    }

    internal sealed class SettingsStore
    {
        private readonly string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "鲨壁");

        private string SettingsPath
        {
            get { return Path.Combine(_directory, "settings.ini"); }
        }

        public AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return settings;
                }

                foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, separator);
                    var value = line.Substring(separator + 1);
                    if (key == "video")
                    {
                        try
                        {
                            settings.VideoPath = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                        }
                        catch (FormatException)
                        {
                            settings.VideoPath = null;
                        }
                    }
                    else if (key == "scale" && IsValidScaleMode(value))
                    {
                        settings.ScaleMode = value;
                    }
                    else if (key == "muted")
                    {
                        bool muted;
                        if (bool.TryParse(value, out muted))
                        {
                            settings.IsMuted = muted;
                        }
                    }
                    else if (key == "steamcmd")
                    {
                        try
                        {
                            settings.SteamCmdPath = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                        }
                        catch (FormatException)
                        {
                            settings.SteamCmdPath = null;
                        }
                    }
                    else if (key == "steamuser")
                    {
                        try
                        {
                            settings.SteamUserName = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                        }
                        catch (FormatException)
                        {
                            settings.SteamUserName = null;
                        }
                    }
                }
            }
            catch (IOException)
            {
                return new AppSettings();
            }
            catch (UnauthorizedAccessException)
            {
                return new AppSettings();
            }

            if (!SteamCmdService.IsValidSteamUserName(settings.SteamUserName))
            {
                settings.SteamUserName = SteamCmdService.DefaultSteamUserName;
            }

            return settings;
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(_directory);
            var video = string.IsNullOrWhiteSpace(settings.VideoPath)
                ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.VideoPath));
            var content = string.Join(Environment.NewLine, new[]
            {
                "video=" + video,
                "scale=" + (IsValidScaleMode(settings.ScaleMode) ? settings.ScaleMode : "UniformToFill"),
                "muted=" + settings.IsMuted,
                "steamcmd=" + Encode(settings.SteamCmdPath),
                "steamuser=" + Encode(settings.SteamUserName)
            });

            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }

        private static bool IsValidScaleMode(string value)
        {
            return value == "UniformToFill" || value == "Uniform" || value == "Fill";
        }

        private static string Encode(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
    }
}
