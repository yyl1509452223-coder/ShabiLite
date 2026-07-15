using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ShabiLite.Interop;
using ShabiLite.Services;

namespace ShabiLite
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SettingsStore _settingsStore;
        private readonly string _libraryDirectory;
        private bool _remoteSettingsUnlocked;

        internal SettingsWindow(AppSettings settings, SettingsStore settingsStore, string libraryDirectory)
        {
            InitializeComponent();
            _settings = settings;
            _settingsStore = settingsStore;
            _libraryDirectory = libraryDirectory;

            RemoteServerUrlTextBox.Text = settings.RemoteServerUrl ?? string.Empty;
            RemoteServerKeyTextBox.Text = settings.RemoteServerKey ?? string.Empty;
            _remoteSettingsUnlocked = string.IsNullOrWhiteSpace(settings.RemoteSettingsPasswordHash);
            UpdateRemoteSettingsVisibility();
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
            SettingsWindowFrame.BorderThickness = WindowState == WindowState.Maximized
                ? new Thickness(0)
                : new Thickness(1);
        }

        private void UnlockRemoteSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            UnlockRemoteSettings();
        }

        private void RemoteSettingsPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            UnlockRemoteSettings();
            e.Handled = true;
        }

        private void UnlockRemoteSettings()
        {
            if (!RemoteSettingsSecurity.VerifyPassword(
                    RemoteSettingsPasswordBox.Password,
                    _settings.RemoteSettingsPasswordHash))
            {
                RemoteSettingsLockStatusText.Text = "密码不正确，无法查看服务器设置。";
                RemoteSettingsPasswordBox.SelectAll();
                RemoteSettingsPasswordBox.Focus();
                return;
            }

            _remoteSettingsUnlocked = true;
            RemoteSettingsPasswordBox.Clear();
            RemoteSettingsLockStatusText.Text = string.Empty;
            UpdateRemoteSettingsVisibility();
            RemoteServerUrlTextBox.Focus();
        }

        private void UpdateRemoteSettingsVisibility()
        {
            RemoteSettingsLockPanel.Visibility = _remoteSettingsUnlocked ? Visibility.Collapsed : Visibility.Visible;
            RemoteServerFields.Visibility = _remoteSettingsUnlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void TestRemoteConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestRemoteConnectionButton.IsEnabled = false;
            RemoteConnectionStatusText.Text = "正在连接服务器…";
            RemoteConnectionStatusText.Foreground = Brush("#7358A6");
            try
            {
                var message = await RemoteDownloadService.TestConnectionAsync(
                    RemoteServerUrlTextBox.Text,
                    RemoteServerKeyTextBox.Text);
                RemoteConnectionStatusText.Text = message;
                RemoteConnectionStatusText.Foreground = Brush("#2E7D50");
            }
            catch (Exception exception)
            {
                RemoteConnectionStatusText.Text = "连接失败：" + exception.Message;
                RemoteConnectionStatusText.Foreground = Brush("#B43D51");
            }
            finally
            {
                TestRemoteConnectionButton.IsEnabled = true;
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
            try
            {
                SaveValues();
                DialogResult = true;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "无法保存设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveValues()
        {
            if (_remoteSettingsUnlocked)
            {
                _settings.RemoteServerUrl = RemoteDownloadService.NormalizeServerUrl(RemoteServerUrlTextBox.Text);
                _settings.RemoteServerKey = (RemoteServerKeyTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(_settings.RemoteServerKey))
                {
                    throw new InvalidOperationException("请输入远程服务器访问密钥。");
                }
            }
            else if (string.IsNullOrWhiteSpace(_settings.RemoteServerUrl) || string.IsNullOrWhiteSpace(_settings.RemoteServerKey))
            {
                throw new InvalidOperationException("远程服务器尚未配置，请先输入管理密码解锁设置。 ");
            }

            _settingsStore.Save(_settings);
        }

        private static SolidColorBrush Brush(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
    }
}
