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
        public string RemoteServerUrl { get; set; }
        public string RemoteServerKey { get; set; }
        public string RemoteSettingsPasswordHash { get; set; }
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
                if (File.Exists(SettingsPath))
                {
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
                        else if (key == "remoteurl")
                        {
                            settings.RemoteServerUrl = Decode(value);
                        }
                        else if (key == "remotekey")
                        {
                            settings.RemoteServerKey = Decode(value);
                        }
                        else if (key == "remotepasswordhash")
                        {
                            settings.RemoteSettingsPasswordHash = Decode(value);
                        }
                    }
                }
            }
            catch (IOException)
            {
                settings = new AppSettings();
            }
            catch (UnauthorizedAccessException)
            {
                settings = new AppSettings();
            }

            ApplyBundledRemoteProfile(settings);

            return settings;
        }

        private static void ApplyBundledRemoteProfile(AppSettings settings)
        {
            var profilePath = Path.Combine(AppContext.BaseDirectory, "remote-access.ini");
            if (!File.Exists(profilePath))
            {
                return;
            }

            try
            {
                string url = null;
                string apiKey = null;
                string passwordHash = null;
                foreach (var line in File.ReadAllLines(profilePath, Encoding.UTF8))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
                    {
                        url = value;
                    }
                    else if (key.Equals("key", StringComparison.OrdinalIgnoreCase))
                    {
                        apiKey = value;
                    }
                    else if (key.Equals("passwordhash", StringComparison.OrdinalIgnoreCase))
                    {
                        passwordHash = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(apiKey))
                {
                    if (string.IsNullOrWhiteSpace(settings.RemoteServerUrl))
                    {
                        settings.RemoteServerUrl = url;
                    }
                    if (string.IsNullOrWhiteSpace(settings.RemoteServerKey))
                    {
                        settings.RemoteServerKey = apiKey;
                    }
                }
                if (!string.IsNullOrWhiteSpace(passwordHash))
                {
                    settings.RemoteSettingsPasswordHash = passwordHash;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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
                "remoteurl=" + Encode(settings.RemoteServerUrl),
                "remotekey=" + Encode(settings.RemoteServerKey),
                "remotepasswordhash=" + Encode(settings.RemoteSettingsPasswordHash)
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

        private static string Decode(string value)
        {
            try
            {
                return string.IsNullOrWhiteSpace(value)
                    ? null
                    : Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
