using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ShabiLite.Interop;
using ShabiLite.Services;

namespace ShabiLite
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SettingsStore _settingsStore;
        private readonly string _libraryDirectory;

        internal SettingsWindow(AppSettings settings, SettingsStore settingsStore, string libraryDirectory)
        {
            InitializeComponent();
            _settings = settings;
            _settingsStore = settingsStore;
            _libraryDirectory = libraryDirectory;

            SteamUserNameTextBox.Text = SteamCmdService.IsValidSteamUserName(settings.SteamUserName)
                ? settings.SteamUserName
                : SteamCmdService.DefaultSteamUserName;
            var steamCmdPath = SteamCmdService.FindSteamCmd(settings.SteamCmdPath);
            SteamCmdPathTextBox.Text = steamCmdPath ?? string.Empty;
            SourceInitialized += delegate { WindowAppearance.ApplyDarkTitleBar(this); };
            Loaded += delegate { UpdateWindowFrameState(); };
        }

        private void CloseSettingsWindowButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SettingsWindow_StateChanged(object sender, EventArgs e)
        {
            UpdateWindowFrameState();
        }

        private void UpdateWindowFrameState()
        {
            var maximized = WindowState == WindowState.Maximized;
            SettingsWindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        }

        private void BrowseSteamCmdButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 steamcmd.exe",
                Filter = "SteamCMD (steamcmd.exe)|steamcmd.exe|应用程序 (*.exe)|*.exe",
                FileName = "steamcmd.exe",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                SteamCmdPathTextBox.Text = dialog.FileName;
                LoginStatusText.Text = "SteamCMD 已选择，保存后即可使用。";
                LoginStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3569A8"));
            }
        }

        private void LoginSteamCmdButton_Click(object sender, RoutedEventArgs e)
        {
            var userName = (SteamUserNameTextBox.Text ?? string.Empty).Trim();
            if (!SteamCmdService.IsValidSteamUserName(userName))
            {
                LoginStatusText.Text = "请输入有效的 Steam 账号登录名。";
                LoginStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B43D51"));
                SteamUserNameTextBox.Focus();
                return;
            }

            var steamCmdPath = SteamCmdService.FindSteamCmd(SteamCmdPathTextBox.Text);
            if (steamCmdPath == null)
            {
                LoginStatusText.Text = "请先选择 steamcmd.exe。";
                LoginStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B43D51"));
                return;
            }

            try
            {
                SaveValues();
                SteamCmdService.OpenLoginWindow(steamCmdPath, userName);
                LoginStatusText.Text = "登录窗口已打开。按提示输入密码和 Steam Guard；看到 Logged in...OK 后输入 quit。";
                LoginStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E7D50"));
            }
            catch (Exception exception)
            {
                LoginStatusText.Text = "无法打开登录：" + exception.Message;
                LoginStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B43D51"));
            }
        }

        private void OpenLibraryButton_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(_libraryDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _libraryDirectory + "\"") { UseShellExecute = true });
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(Log.Path))
            {
                MessageBox.Show(this, "当前还没有日志文件。", "鲨壁设置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(Log.Path) { UseShellExecute = true });
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveValues();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveValues()
        {
            var userName = (SteamUserNameTextBox.Text ?? string.Empty).Trim();
            if (userName.Length == 0)
            {
                userName = SteamCmdService.DefaultSteamUserName;
                SteamUserNameTextBox.Text = userName;
            }
            if (!SteamCmdService.IsValidSteamUserName(userName))
            {
                throw new InvalidOperationException("Steam 用户名格式无效。");
            }

            _settings.SteamUserName = userName;
            _settings.SteamCmdPath = SteamCmdService.FindSteamCmd(SteamCmdPathTextBox.Text) ?? SteamCmdPathTextBox.Text;
            _settingsStore.Save(_settings);
        }
    }
}
