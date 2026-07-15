using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShabiLite.Services
{
    internal sealed class SteamCmdDownloadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Output { get; set; }
        public string WorkshopDirectory { get; set; }
        public string VideoPath { get; set; }
        public string Title { get; set; }
    }

    internal sealed class SteamCmdProgress
    {
        public int Percent { get; set; }
        public string Message { get; set; }
    }

    internal static class SteamCmdService
    {
        private const string AppId = "431960";
        public const string DefaultSteamUserName = "1509452223";

        public static bool TryExtractWorkshopId(string input, out string workshopId)
        {
            workshopId = null;
            var value = (input ?? string.Empty).Trim();
            if (Regex.IsMatch(value, @"^\d{6,20}$"))
            {
                workshopId = value;
                return true;
            }

            var match = Regex.Match(value, @"(?:[?&]id=|/filedetails/)(\d{6,20})", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            workshopId = match.Groups[1].Value;
            return true;
        }

        public static string FindSteamCmd(string savedPath)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var managedPath = GetManagedSteamCmdPath();
            var candidates = new[]
            {
                savedPath,
                managedPath,
                Path.Combine(AppContext.BaseDirectory, "steamcmd.exe"),
                Path.Combine(profile, "Downloads", "steamcmd.exe"),
                Path.Combine(profile, "Downloads", "steamcmd", "steamcmd.exe"),
                @"C:\steamcmd\steamcmd.exe"
            };

            // Preserve an existing login cache when SteamCMD is already in an
            // ASCII-only directory. SteamCMD cannot start from Chinese paths.
            var usablePath = candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsAsciiPath(path));
            if (usablePath != null)
            {
                return usablePath;
            }

            var sourcePath = candidates.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (sourcePath == null)
            {
                return null;
            }

            try
            {
                var directory = Path.GetDirectoryName(managedPath);
                Directory.CreateDirectory(directory);
                if (!File.Exists(managedPath))
                {
                    File.Copy(sourcePath, managedPath, false);
                    Log.Write("SteamCMD 已复制到英文运行目录：" + managedPath);
                }
                return managedPath;
            }
            catch (Exception exception)
            {
                Log.Write("SteamCMD 无法迁移到英文目录：" + exception.Message);
                return null;
            }
        }

        public static bool IsValidSteamUserName(string userName)
        {
            return Regex.IsMatch((userName ?? string.Empty).Trim(), @"^[A-Za-z0-9_-]{2,64}$");
        }

        public static void OpenLoginWindow(string steamCmdPath, string userName)
        {
            if (!IsValidSteamUserName(userName))
            {
                throw new ArgumentException("Steam 用户名格式无效。");
            }

            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandInterpreter) || !File.Exists(commandInterpreter))
            {
                commandInterpreter = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "cmd.exe");
            }

            if (!IsAsciiPath(steamCmdPath))
            {
                throw new InvalidOperationException("SteamCMD 必须位于纯英文路径，请重新打开设置让软件自动迁移。");
            }

            var loginScriptPath = Path.Combine(
                Path.GetDirectoryName(steamCmdPath),
                "shabi-login.cmd");
            var loginScript = string.Join("\r\n", new[]
            {
                "@echo off",
                "title Shabi Steam Login",
                "echo Steam account: " + userName.Trim(),
                "echo Enter the password and Steam Guard code when prompted.",
                "echo After Logged in...OK appears, type quit.",
                "echo.",
                "\"" + steamCmdPath + "\" +login " + userName.Trim(),
                "echo.",
                "echo SteamCMD closed. You can close this window.",
                string.Empty
            });
            File.WriteAllText(loginScriptPath, loginScript, Encoding.ASCII);

            // Keep the console open so first-time users can enter their password
            // and Steam Guard code, and so update/login errors do not flash past.
            // The user closes it by typing "quit" after Steam reports login OK.
            Process.Start(new ProcessStartInfo
            {
                FileName = commandInterpreter,
                WorkingDirectory = Path.GetDirectoryName(steamCmdPath),
                Arguments = "/D /K call \"" + loginScriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = false
            });
        }

        private static string GetManagedSteamCmdPath()
        {
            var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(commonData, "ShabiLite", "SteamCMD", "steamcmd.exe");
        }

        private static bool IsAsciiPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.All(character => character <= 127);
        }

        public static Task<SteamCmdDownloadResult> DownloadAsync(
            string steamCmdPath,
            string workshopId,
            string steamUserName,
            IProgress<SteamCmdProgress> progress)
        {
            return Task.Run(delegate
            {
                Report(progress, 5, "正在启动 SteamCMD…");
                var workingDirectory = Path.GetDirectoryName(steamCmdPath);
                var expectedDirectory = Path.Combine(
                    workingDirectory,
                    "steamapps",
                    "workshop",
                    "content",
                    AppId,
                    workshopId);
                var output = new StringBuilder();
                var loginName = IsValidSteamUserName(steamUserName) ? steamUserName.Trim() : "anonymous";

                var startInfo = new ProcessStartInfo
                {
                    FileName = steamCmdPath,
                    WorkingDirectory = workingDirectory,
                    Arguments = "+@ShutdownOnFailedCommand 1 +@NoPromptForPassword 1 +login " + loginName + " +workshop_download_item " + AppId + " " + workshopId + " validate +quit",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                try
                {
                    using (var process = new Process { StartInfo = startInfo })
                    {
                        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                lock (output) { output.AppendLine(e.Data); }
                                ReportLine(progress, e.Data, workshopId);
                            }
                        };
                        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                        {
                            if (e.Data != null)
                            {
                                lock (output) { output.AppendLine(e.Data); }
                                ReportLine(progress, e.Data, workshopId);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        if (!process.WaitForExit(15 * 60 * 1000))
                        {
                            try { process.Kill(); } catch { }
                            Report(progress, 100, "下载失败：SteamCMD 等待超时。");
                            return Failure("SteamCMD 下载超时，请检查网络后重试。", output.ToString(), expectedDirectory);
                        }
                        process.WaitForExit();

                        var text = output.ToString();
                        var successText = text.IndexOf("Success. Downloaded item", StringComparison.OrdinalIgnoreCase) >= 0;
                        var directoryExists = Directory.Exists(expectedDirectory);
                        if (process.ExitCode != 0 || (!successText && !directoryExists))
                        {
                            var reason = ExtractFailure(text, loginName);
                            Report(progress, 100, "下载失败：" + reason);
                            return Failure("下载失败：" + reason, text, expectedDirectory);
                        }

                        var videoPath = directoryExists
                            ? Directory.GetFiles(expectedDirectory, "*.mp4", SearchOption.AllDirectories).FirstOrDefault()
                            : null;
                        var title = ReadWorkshopTitle(expectedDirectory);
                        var successMessage = videoPath == null
                            ? "下载完成，但没有找到 MP4。该项目可能不是视频壁纸。"
                            : string.IsNullOrWhiteSpace(title)
                                ? "下载成功，已自动加入壁纸库。"
                                : "下载成功，已按创意工坊标题加入壁纸库。";
                        Report(progress, 100, successMessage);
                        return new SteamCmdDownloadResult
                        {
                            Success = true,
                            Message = successMessage,
                            Output = text,
                            WorkshopDirectory = expectedDirectory,
                            VideoPath = videoPath,
                            Title = title
                        };
                    }
                }
                catch (Exception exception)
                {
                    Report(progress, 100, "下载失败：无法运行 SteamCMD。" );
                    return Failure("无法运行 SteamCMD：" + exception.Message, output.ToString(), expectedDirectory);
                }
            });
        }

        private static SteamCmdDownloadResult Failure(string message, string output, string directory)
        {
            return new SteamCmdDownloadResult
            {
                Success = false,
                Message = message,
                Output = output,
                WorkshopDirectory = directory
            };
        }

        private static string ReadWorkshopTitle(string workshopDirectory)
        {
            try
            {
                var projectPath = Path.Combine(workshopDirectory, "project.json");
                if (!File.Exists(projectPath))
                {
                    return null;
                }

                var json = File.ReadAllText(projectPath, Encoding.UTF8);
                var match = Regex.Match(
                    json,
                    "\\\"title\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
                    RegexOptions.IgnoreCase);
                return match.Success ? DecodeJsonString(match.Groups[1].Value).Trim() : null;
            }
            catch (Exception exception)
            {
                Log.Write("无法读取创意工坊标题：" + exception.Message);
                return null;
            }
        }

        private static string DecodeJsonString(string value)
        {
            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character != '\\' || index + 1 >= value.Length)
                {
                    result.Append(character);
                    continue;
                }

                var escaped = value[++index];
                switch (escaped)
                {
                    case '\"': result.Append('\"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (index + 4 < value.Length)
                        {
                            int codePoint;
                            if (int.TryParse(
                                value.Substring(index + 1, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out codePoint))
                            {
                                result.Append((char)codePoint);
                                index += 4;
                                break;
                            }
                        }
                        result.Append('u');
                        break;
                    default:
                        result.Append(escaped);
                        break;
                }
            }
            return result.ToString();
        }

        private static string ExtractFailure(string output, string loginName)
        {
            if (output.IndexOf("No Connection", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return loginName == "anonymous"
                    ? "匿名登录被创意工坊拒绝（No Connection）。请在右侧填写拥有 Wallpaper Engine 的 Steam 用户名并完成一次登录授权。"
                    : "Steam 返回 No Connection。请确认该账号拥有 Wallpaper Engine；若令牌已过期，请重新点击登录授权。";
            }
            if (output.IndexOf("Account Logon Denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("Invalid Password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                output.IndexOf("Steam Guard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Steam 登录令牌无效或已过期，请重新点击右侧“登录授权”。";
            }
            if (output.IndexOf("No subscription", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "匿名账号无权下载这个项目，可能需要使用拥有 Wallpaper Engine 的 Steam 账号。";
            }
            if (output.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SteamCMD 返回 Failure，请确认链接有效并稍后重试。";
            }
            return "SteamCMD 未返回成功结果。";
        }

        private static void ReportLine(IProgress<SteamCmdProgress> progress, string line, string workshopId)
        {
            if (line.IndexOf("Loading Steam API", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Report(progress, 10, "正在加载 Steam API…");
            }
            else if (line.IndexOf("Connecting anonymously", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Report(progress, 20, "正在匿名连接 Steam…");
            }
            else if (line.IndexOf("Waiting for client config", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Report(progress, 30, "正在获取 Steam 客户端配置…");
            }
            else if (line.IndexOf("Waiting for user info", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Report(progress, 40, "正在获取 Steam 用户信息…");
            }
            else if (line.IndexOf("Downloading item", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Report(progress, 55, "正在下载 Workshop " + workshopId + "…");
            }
        }

        private static void Report(IProgress<SteamCmdProgress> progress, int percent, string message)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new SteamCmdProgress
            {
                Percent = Math.Max(0, Math.Min(100, percent)),
                Message = message
            });
        }
    }
}
