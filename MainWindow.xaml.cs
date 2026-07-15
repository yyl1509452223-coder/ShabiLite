using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShabiLite.Interop;
using ShabiLite.Services;

namespace ShabiLite
{
    public partial class MainWindow : Window
    {
        private const string WorkshopBrowseUrl = "https://steamcommunity.com/workshop/browse/?appid=431960&browsesort=trend&section=readytouseitems&p=1&num_per_page=30&days=7&requiredtags%5B%5D=Everyone&requiredtags%5B%5D=Video";

        private readonly SettingsStore _settingsStore = new SettingsStore();
        private readonly WallpaperPlayer _wallpaper = new WallpaperPlayer();
        private readonly TrayIconService _trayIcon;
        private readonly AppSettings _settings;
        private readonly ObservableCollection<WallpaperItem> _wallpapers = new ObservableCollection<WallpaperItem>();
        private bool _allowClose;
        private bool _isInitializing = true;
        private bool _isDownloading;
        private string _selectedVideoPath;
        private string _appliedVideoPath;
        private int _downloadProgressPercent;
        private string LibraryDirectory { get { return Path.Combine(AppContext.BaseDirectory, "Wallpapers"); } }

        public MainWindow()
        {
            InitializeComponent();
            WallpaperList.ItemsSource = _wallpapers;
            _settings = _settingsStore.Load();
            _trayIcon = new TrayIconService();

            _trayIcon.RestoreRequested += delegate { Dispatcher.BeginInvoke(new Action(ShowFromTray)); };
            _trayIcon.TogglePlaybackRequested += delegate { Dispatcher.BeginInvoke(new Action(TogglePlayback)); };
            _trayIcon.ExitRequested += delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); };
            _wallpaper.PlaybackStarted += Wallpaper_PlaybackStarted;
            _wallpaper.PlaybackFailed += Wallpaper_PlaybackFailed;

            SourceInitialized += delegate { WindowAppearance.ApplyDarkTitleBar(this); };
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWindowFrameState();
            SelectScaleMode(_settings.ScaleMode);
            MuteCheckBox.IsChecked = _settings.IsMuted;
            _wallpaper.SetMuted(_settings.IsMuted);
            LoadLibrary();

            if (!string.IsNullOrWhiteSpace(_settings.VideoPath) && File.Exists(_settings.VideoPath))
            {
                SelectVideo(_settings.VideoPath, "上次选择", false);
                ShowStatus("已恢复上次选择的视频，点击“运行壁纸”开始。", StatusKind.Info);
            }
            else
            {
                if (_wallpapers.Count > 0)
                {
                    WallpaperList.SelectedItem = _wallpapers[0];
                    ShowStatus("壁纸库已载入，选择卡片后点击“运行壁纸”。", StatusKind.Neutral);
                }
                else
                {
                    ShowStatus("可以打开本地 MP4，或先浏览 Steam 创意工坊。", StatusKind.Neutral);
                }
            }

            _isInitializing = false;
        }

        private void BrowseWorkshopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(WorkshopBrowseUrl) { UseShellExecute = true });
                ShowStatus("已在浏览器打开创意工坊。复制壁纸详情页 URL 后粘贴到顶部。", StatusKind.Info);
            }
            catch (Exception exception)
            {
                ShowStatus("无法打开浏览器：" + exception.Message, StatusKind.Error);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
            {
                return;
            }

            string workshopId;
            if (!SteamCmdService.TryExtractWorkshopId(WorkshopUrlTextBox.Text, out workshopId))
            {
                ShowStatus("请输入有效的创意工坊详情页 URL，或直接输入 Workshop ID。", StatusKind.Error);
                WorkshopUrlTextBox.Focus();
                return;
            }

            var steamCmdPath = SteamCmdService.FindSteamCmd(_settings.SteamCmdPath);
            if (steamCmdPath == null)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择 steamcmd.exe",
                    Filter = "SteamCMD (steamcmd.exe)|steamcmd.exe|应用程序 (*.exe)|*.exe",
                    FileName = "steamcmd.exe",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) != true)
                {
                    ShowStatus("下载需要 SteamCMD。选择 steamcmd.exe 后即可继续。", StatusKind.Info);
                    return;
                }
                steamCmdPath = dialog.FileName;
            }

            var steamUserName = SteamCmdService.IsValidSteamUserName(_settings.SteamUserName)
                ? _settings.SteamUserName.Trim()
                : string.Empty;

            _settings.SteamCmdPath = steamCmdPath;
            _settings.SteamUserName = steamUserName;
            SaveSettings();
            SetDownloadState(true);
            ShowDownloadProgress(3, "正在准备下载 Workshop " + workshopId + "…", StatusKind.Loading);

            var progress = new Progress<SteamCmdProgress>(update =>
                ShowDownloadProgress(update.Percent, update.Message, StatusKind.Loading));
            var result = await SteamCmdService.DownloadAsync(steamCmdPath, workshopId, steamUserName, progress);
            SetDownloadState(false);
            Log.Write("SteamCMD " + workshopId + "：" + result.Message + Environment.NewLine + Tail(result.Output, 4000));

            if (!result.Success)
            {
                ShowDownloadProgress(100, result.Message, StatusKind.Error);
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.VideoPath) && File.Exists(result.VideoPath))
            {
                SelectVideo(result.VideoPath, "创意工坊 " + workshopId, true, result.Title);
                var titleMessage = string.IsNullOrWhiteSpace(result.Title)
                    ? result.Message
                    : result.Message + " 文件名：" + result.Title;
                ShowDownloadProgress(100, titleMessage, StatusKind.Success);
            }
            else
            {
                ShowDownloadProgress(100, result.Message + " 下载目录：" + result.WorkshopDirectory, StatusKind.Info);
            }
        }

        private void WorkshopUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            WorkshopUrlHint.Visibility = string.IsNullOrWhiteSpace(WorkshopUrlTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "打开动态壁纸",
                Filter = "MP4 视频 (*.mp4)|*.mp4",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                foreach (var path in dialog.FileNames)
                {
                    SelectVideo(path, "本地文件", true);
                }
                ShowStatus("文件已复制到软件壁纸库，点击卡片选择并运行。", StatusKind.Info);
            }
        }

        private void RunPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_wallpaper.IsActive && string.Equals(_appliedVideoPath, _selectedVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                TogglePlayback();
                return;
            }

            ApplySelectedWallpaper();
        }

        private void ApplySelectedWallpaper()
        {
            if (string.IsNullOrWhiteSpace(_selectedVideoPath))
            {
                ShowStatus("请先打开或下载一个 MP4 视频。", StatusKind.Error);
                return;
            }
            if (!File.Exists(_selectedVideoPath))
            {
                ShowStatus("找不到所选视频，请重新打开文件。", StatusKind.Error);
                return;
            }

            try
            {
                RunPauseButton.IsEnabled = false;
                ShowStatus("正在连接桌面并启动视频…", StatusKind.Loading);
                Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Render);
                _wallpaper.Start(_selectedVideoPath);
                _appliedVideoPath = _selectedVideoPath;
                _settings.VideoPath = _selectedVideoPath;
                SaveSettings();
            }
            catch (Exception exception)
            {
                RunPauseButton.IsEnabled = true;
                RunPauseButton.Content = "运行壁纸";
                ShowStatus("无法启动视频壁纸：" + exception.Message, StatusKind.Error);
            }
        }

        private void TogglePlayback()
        {
            if (!_wallpaper.IsActive)
            {
                ShowFromTray();
                ShowStatus("还没有正在运行的壁纸。", StatusKind.Info);
                return;
            }

            if (_wallpaper.IsPaused)
            {
                _wallpaper.Resume();
                RunPauseButton.Content = "暂停壁纸";
                _trayIcon.SetPaused(false);
                ShowStatus("视频壁纸正在桌面图标后循环播放。", StatusKind.Success);
            }
            else
            {
                _wallpaper.Pause();
                RunPauseButton.Content = "继续壁纸";
                _trayIcon.SetPaused(true);
                ShowStatus("视频壁纸已暂停。", StatusKind.Info);
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = GetDroppedMp4(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            var path = GetDroppedMp4(e.Data);
            if (path == null)
            {
                ShowStatus("请拖入一个有效的 MP4 视频文件。", StatusKind.Error);
                return;
            }
            SelectVideo(path, "拖入文件", true);
            ShowStatus("文件已复制到软件壁纸库，点击“运行壁纸”应用。", StatusKind.Info);
        }

        private void SelectVideo(string path, string sourceLabel, bool save, string preferredFileName = null)
        {
            try
            {
                var item = ImportToLibrary(path, sourceLabel, preferredFileName);
                WallpaperList.SelectedItem = item;
                WallpaperList.ScrollIntoView(item);
                UpdateLibraryState();

                if (save)
                {
                    _settings.VideoPath = item.Path;
                    SaveSettings();
                }
                Log.Write("选择视频：" + item.Path);
            }
            catch (Exception exception)
            {
                ShowStatus("无法加入壁纸库：" + exception.Message, StatusKind.Error);
            }
        }

        private void LoadLibrary()
        {
            try
            {
                Directory.CreateDirectory(LibraryDirectory);
                foreach (var path in Directory.GetFiles(LibraryDirectory, "*.mp4", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTime))
                {
                    _wallpapers.Add(CreateWallpaperItem(path, "壁纸库"));
                }
            }
            catch (Exception exception)
            {
                ShowStatus("无法读取软件壁纸库：" + exception.Message, StatusKind.Error);
            }
            UpdateLibraryState();
        }

        private WallpaperItem ImportToLibrary(string sourcePath, string sourceLabel, string preferredFileName = null)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到所选视频。", sourcePath);
            }

            Directory.CreateDirectory(LibraryDirectory);
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var libraryFullPath = Path.GetFullPath(LibraryDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destinationPath;

            if (sourceFullPath.StartsWith(libraryFullPath, StringComparison.OrdinalIgnoreCase))
            {
                destinationPath = sourceFullPath;
            }
            else
            {
                var libraryFileName = string.IsNullOrWhiteSpace(preferredFileName)
                    ? Path.GetFileName(sourceFullPath)
                    : MakeSafeLibraryFileName(preferredFileName) + ".mp4";
                destinationPath = GetAvailableLibraryPath(libraryFileName, new FileInfo(sourceFullPath).Length);
                if (!File.Exists(destinationPath))
                {
                    File.Copy(sourceFullPath, destinationPath, false);
                }
            }

            var existing = _wallpapers.FirstOrDefault(item => string.Equals(item.Path, destinationPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            var wallpaper = CreateWallpaperItem(destinationPath, sourceLabel);
            _wallpapers.Insert(0, wallpaper);
            return wallpaper;
        }

        private string GetAvailableLibraryPath(string fileName, long sourceLength)
        {
            var candidate = Path.Combine(LibraryDirectory, fileName);
            if (!File.Exists(candidate) || new FileInfo(candidate).Length == sourceLength)
            {
                return candidate;
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var index = 2; index < 10000; index++)
            {
                candidate = Path.Combine(LibraryDirectory, name + " (" + index + ")" + extension);
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new IOException("同名壁纸过多，请先整理壁纸库。");
        }

        private static string MakeSafeLibraryFileName(string title)
        {
            var replacements = new Dictionary<char, char>
            {
                { '\\', '＼' },
                { '/', '／' },
                { ':', '：' },
                { '*', '＊' },
                { '?', '？' },
                { '\"', '\'' },
                { '<', '＜' },
                { '>', '＞' },
                { '|', '｜' }
            };
            var safeName = new StringBuilder();
            foreach (var character in title.Trim())
            {
                char replacement;
                if (replacements.TryGetValue(character, out replacement))
                {
                    safeName.Append(replacement);
                }
                else if (!char.IsControl(character))
                {
                    safeName.Append(character);
                }
            }

            var result = Regex.Replace(safeName.ToString(), @"\s+", " ").Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(result))
            {
                return "Workshop Wallpaper";
            }
            return result.Length <= 150 ? result : result.Substring(0, 150).TrimEnd();
        }

        private static WallpaperItem CreateWallpaperItem(string path, string sourceLabel)
        {
            return new WallpaperItem
            {
                FileName = Path.GetFileNameWithoutExtension(path),
                Path = path,
                SourceLabel = sourceLabel,
                Thumbnail = VideoThumbnailProvider.GetThumbnail(path, 316, 184)
            };
        }

        private void WallpaperList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = WallpaperList.SelectedItem as WallpaperItem;
            if (item == null)
            {
                _selectedVideoPath = null;
                CurrentSelectionText.Text = "尚未选择壁纸";
                CurrentSelectionText.ToolTip = null;
                UpdateLibraryState();
                return;
            }

            _selectedVideoPath = item.Path;
            CurrentSelectionText.Text = item.FileName;
            CurrentSelectionText.ToolTip = item.Path;
            _settings.VideoPath = item.Path;
            if (!_isInitializing)
            {
                SaveSettings();
            }

            if (_wallpaper.IsActive && !string.Equals(_appliedVideoPath, item.Path, StringComparison.OrdinalIgnoreCase))
            {
                RunPauseButton.Content = "应用新壁纸";
            }
            else if (!_wallpaper.IsActive)
            {
                RunPauseButton.Content = "运行壁纸";
            }
            UpdateLibraryState();
        }

        private void DeleteWallpaperMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var item = menuItem == null ? null : menuItem.Tag as WallpaperItem;
            if (item == null)
            {
                item = WallpaperList.SelectedItem as WallpaperItem;
            }
            if (item == null)
            {
                return;
            }

            if (MessageBox.Show(this,
                    "确定从软件壁纸库删除“" + item.FileName + "”吗？\n\n这会删除 Wallpapers 文件夹中的副本，不影响原始文件。",
                    "删除壁纸",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                if (_wallpaper.IsActive && string.Equals(_appliedVideoPath, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    _wallpaper.Stop();
                    _appliedVideoPath = null;
                    _trayIcon.SetPlaybackAvailable(false);
                    RunPauseButton.Content = "运行壁纸";
                }

                File.Delete(item.Path);
                _wallpapers.Remove(item);
                if (_wallpapers.Count > 0)
                {
                    WallpaperList.SelectedItem = _wallpapers[0];
                }
                else
                {
                    _selectedVideoPath = null;
                    _settings.VideoPath = null;
                    SaveSettings();
                }
                UpdateLibraryState();
                ShowStatus("壁纸已从软件库删除。", StatusKind.Info);
            }
            catch (Exception exception)
            {
                ShowStatus("无法删除壁纸：" + exception.Message, StatusKind.Error);
            }
        }

        private void UpdateLibraryState()
        {
            PreviewEmptyState.Visibility = _wallpapers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WallpaperList.Visibility = _wallpapers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            LibraryCountText.Text = _wallpapers.Count + " 张壁纸" + (WallpaperList.SelectedItem == null ? string.Empty : " · 已选择");
        }

        private void WallpaperTile_MouseEnter(object sender, MouseEventArgs e)
        {
            var container = sender as ListBoxItem;
            var item = container == null ? null : container.DataContext as WallpaperItem;
            if (container == null || item == null || !File.Exists(item.Path))
            {
                return;
            }

            var preview = FindVisualChild<MediaElement>(container);
            if (preview == null)
            {
                return;
            }

            try
            {
                preview.Stop();
                preview.Source = null;
                preview.Opacity = 0;
                preview.Visibility = Visibility.Visible;
                preview.Source = new Uri(item.Path, UriKind.Absolute);
                preview.Position = TimeSpan.Zero;
                preview.Play();
            }
            catch
            {
                preview.Visibility = Visibility.Collapsed;
            }
        }

        private void WallpaperTile_MouseLeave(object sender, MouseEventArgs e)
        {
            var container = sender as ListBoxItem;
            var preview = container == null ? null : FindVisualChild<MediaElement>(container);
            if (preview == null)
            {
                return;
            }
            preview.Stop();
            preview.Source = null;
            preview.Opacity = 0;
            preview.Visibility = Visibility.Collapsed;
        }

        private void HoverPreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
        {
            var preview = sender as MediaElement;
            if (preview != null)
            {
                preview.Opacity = 1;
            }
        }

        private void HoverPreviewMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            var preview = sender as MediaElement;
            if (preview == null)
            {
                return;
            }
            preview.Position = TimeSpan.Zero;
            preview.Play();
        }

        private void HoverPreviewMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            var preview = sender as MediaElement;
            if (preview == null)
            {
                return;
            }
            preview.Stop();
            preview.Source = null;
            preview.Opacity = 0;
            preview.Visibility = Visibility.Collapsed;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                var match = child as T;
                if (match != null)
                {
                    return match;
                }
                match = FindVisualChild<T>(child);
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }

        private void ScalingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var mode = GetSelectedScaleMode();
            _wallpaper.SetStretch(ToStretch(mode));
            if (!_isInitializing)
            {
                _settings.ScaleMode = mode;
                SaveSettings();
            }
        }

        private void MuteCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var muted = MuteCheckBox.IsChecked == true;
            _wallpaper.SetMuted(muted);
            if (!_isInitializing)
            {
                _settings.IsMuted = muted;
                SaveSettings();
            }
        }

        private void Wallpaper_PlaybackStarted(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                RunPauseButton.IsEnabled = true;
                RunPauseButton.Content = "暂停壁纸";
                _trayIcon.SetPlaybackAvailable(true);
                _trayIcon.SetPaused(false);
                ShowStatus("视频壁纸正在桌面图标后循环播放。", StatusKind.Success);
            }));
        }

        private void Wallpaper_PlaybackFailed(object sender, PlaybackFailedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                RunPauseButton.IsEnabled = true;
                RunPauseButton.Content = "运行壁纸";
                _trayIcon.SetPlaybackAvailable(false);
                ShowStatus("视频播放失败：" + e.Message, StatusKind.Error);
            }));
        }

        private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
        {
            HideToTray();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings, _settingsStore, LibraryDirectory)
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
            ShowStatus("设置已保存。Steam 登录令牌仅保存在本机 SteamCMD 中。", StatusKind.Info);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }
            e.Cancel = true;
            HideToTray();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray();
                return;
            }

            UpdateWindowFrameState();
        }

        private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void UpdateWindowFrameState()
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeWindowButton.Content = maximized ? "❐" : "□";
            WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        }

        private void HideToTray()
        {
            Hide();
            _trayIcon.ShowMinimizedNotice();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ExitApplication()
        {
            _allowClose = true;
            _trayIcon.Dispose();
            _wallpaper.Dispose();
            Close();
            Application.Current.Shutdown();
        }

        private void SetDownloadState(bool downloading)
        {
            _isDownloading = downloading;
            DownloadButton.IsEnabled = !downloading;
            WorkshopUrlTextBox.IsEnabled = !downloading;
            BrowseWorkshopButton.IsEnabled = !downloading;
            DownloadButton.Content = downloading ? "下载中…" : "下载壁纸";
        }

        private void SaveSettings()
        {
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (IOException)
            {
                ShowStatus("设置暂时无法保存，但本次操作仍可继续。", StatusKind.Info);
            }
        }

        private string GetSelectedScaleMode()
        {
            var item = ScalingComboBox.SelectedItem as ComboBoxItem;
            return item == null ? "UniformToFill" : item.Tag as string ?? "UniformToFill";
        }

        private void SelectScaleMode(string mode)
        {
            var match = ScalingComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, mode, StringComparison.Ordinal));
            ScalingComboBox.SelectedItem = match ?? ScalingComboBox.Items[0];
        }

        private static Stretch ToStretch(string mode)
        {
            if (mode == "Uniform") return Stretch.Uniform;
            return mode == "Fill" ? Stretch.Fill : Stretch.UniformToFill;
        }

        private static string GetDroppedMp4(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return null;
            return File.Exists(files[0]) && string.Equals(Path.GetExtension(files[0]), ".mp4", StringComparison.OrdinalIgnoreCase)
                ? files[0]
                : null;
        }

        private static string Tail(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text ?? string.Empty;
            return text.Substring(text.Length - maxLength);
        }

        private void ShowStatus(string message, StatusKind kind)
        {
            StatusProgressFill.Visibility = Visibility.Collapsed;
            StatusPercentText.Visibility = Visibility.Collapsed;
            _downloadProgressPercent = 0;
            string background;
            string indicator;
            switch (kind)
            {
                case StatusKind.Success: background = "#E9F6EE"; indicator = "#31A063"; break;
                case StatusKind.Error: background = "#FCECEF"; indicator = "#D34A62"; break;
                case StatusKind.Loading: background = "#EFEAFA"; indicator = "#7C5CE5"; break;
                case StatusKind.Info: background = "#EAF2FC"; indicator = "#3E78C5"; break;
                default: background = "#EEEAF3"; indicator = "#80778F"; break;
            }
            StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
            StatusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(indicator));
            StatusTextBlock.Text = message;
        }

        private void ShowDownloadProgress(int percent, string message, StatusKind kind)
        {
            ShowStatus(message, kind);
            _downloadProgressPercent = Math.Max(0, Math.Min(100, percent));
            StatusProgressFill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                kind == StatusKind.Success ? "#2448B879" :
                kind == StatusKind.Error ? "#24D45E72" :
                kind == StatusKind.Info ? "#244C88D0" : "#267C5CE5"));
            StatusProgressFill.Visibility = Visibility.Visible;
            StatusPercentText.Text = _downloadProgressPercent + "%";
            StatusPercentText.Visibility = Visibility.Visible;
            UpdateProgressWidth();
        }

        private void StatusBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateProgressWidth();
        }

        private void UpdateProgressWidth()
        {
            if (StatusProgressFill == null || StatusProgressFill.Visibility != Visibility.Visible)
            {
                return;
            }
            StatusProgressFill.Width = Math.Max(0, StatusBorder.ActualWidth * _downloadProgressPercent / 100.0);
        }

        private enum StatusKind { Neutral, Info, Loading, Success, Error }
    }
}
