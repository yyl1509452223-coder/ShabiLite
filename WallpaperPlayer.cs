using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using ShabiLite.Interop;
using ShabiLite.Services;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ShabiLite
{
    internal sealed class WallpaperPlayer : IDisposable
    {
        private Window _window;
        private VideoView _videoView;
        private LibVLC _libVlc;
        private VlcMediaPlayer _mediaPlayer;
        private Media _media;
        private Stretch _stretch = Stretch.UniformToFill;
        private bool _isMuted;
        private bool _disposed;

        public event EventHandler PlaybackStarted;
        public event EventHandler<PlaybackFailedEventArgs> PlaybackFailed;

        public bool IsActive { get; private set; }
        public bool IsPaused { get; private set; }

        public void Start(string videoPath)
        {
            ThrowIfDisposed();
            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("找不到所选的视频文件。", videoPath);
            }

            if (!string.Equals(Path.GetExtension(videoPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("请选择 MP4 视频文件。");
            }

            Log.Write("使用 LibVLC 加载视频：" + videoPath);
            try
            {
                EnsureWindow();
                PlayAndVerify(videoPath);
                IsActive = true;
                IsPaused = false;
                Log.Write("LibVLC 已开始播放，桌面挂载完成。");
                var handler = PlaybackStarted;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            catch (Exception exception)
            {
                Log.Write("LibVLC 播放失败：" + exception);
                DisposeWindow();
                DesktopHost.RefreshDesktop();
                var handler = PlaybackFailed;
                if (handler != null)
                {
                    handler(this, new PlaybackFailedEventArgs(exception.Message));
                }
                throw;
            }
        }

        public void Pause()
        {
            if (_mediaPlayer == null || !IsActive || IsPaused)
            {
                return;
            }

            _mediaPlayer.SetPause(true);
            IsPaused = true;
        }

        public void Resume()
        {
            if (_mediaPlayer == null || !IsActive || !IsPaused)
            {
                return;
            }

            _mediaPlayer.SetPause(false);
            IsPaused = false;
        }

        public void SetMuted(bool isMuted)
        {
            _isMuted = isMuted;
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Mute = isMuted;
            }
        }

        public void SetStretch(Stretch stretch)
        {
            _stretch = stretch;
            ApplyScaleMode();
        }

        public void Stop()
        {
            DisposeWindow();
            DesktopHost.RefreshDesktop();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWindow();
            DesktopHost.RefreshDesktop();
        }

        private void EnsureWindow()
        {
            if (_window != null)
            {
                return;
            }

            Core.Initialize();
            var libVlc = new LibVLC("--no-video-title-show", "--quiet", "--no-osd");
            var mediaPlayer = new VlcMediaPlayer(libVlc) { Mute = _isMuted };
            var videoView = new VideoView { MediaPlayer = mediaPlayer };
            var window = new Window
            {
                Title = "鲨壁桌面播放器",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Background = Brushes.Black,
                Content = videoView,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = Math.Max(1, SystemParameters.VirtualScreenWidth),
                Height = Math.Max(1, SystemParameters.VirtualScreenHeight)
            };

            _libVlc = libVlc;
            _mediaPlayer = mediaPlayer;
            _videoView = videoView;
            _window = window;

            try
            {
                window.Show();
                var handle = new WindowInteropHelper(window).Handle;
                DesktopHost.AttachAsWallpaper(handle);
                Log.Write("LibVLC 视频窗口已挂到桌面，句柄：" + handle);
            }
            catch
            {
                DisposeWindow();
                throw;
            }
        }

        private void PlayAndVerify(string videoPath)
        {
            var mediaPlayer = _mediaPlayer;
            if (mediaPlayer == null)
            {
                throw new InvalidOperationException("视频播放器尚未初始化。");
            }

            mediaPlayer.Stop();
            if (_media != null)
            {
                _media.Dispose();
            }

            var media = new Media(_libVlc, videoPath, FromType.FromPath);
            media.AddOption(":input-repeat=65535");
            media.AddOption(":no-video-title-show");
            _media = media;

            using (var signal = new ManualResetEventSlim(false))
            {
                var started = false;
                var failed = false;
                EventHandler<EventArgs> playingHandler = delegate
                {
                    started = true;
                    signal.Set();
                };
                EventHandler<EventArgs> errorHandler = delegate
                {
                    failed = true;
                    signal.Set();
                };

                mediaPlayer.Playing += playingHandler;
                mediaPlayer.EncounteredError += errorHandler;
                try
                {
                    if (!mediaPlayer.Play(media))
                    {
                        throw new InvalidOperationException("VLC 无法开始播放所选视频。");
                    }

                    if (!signal.Wait(TimeSpan.FromSeconds(8)) || failed || !started)
                    {
                        throw new InvalidOperationException("视频加载失败或等待播放超时。");
                    }

                    mediaPlayer.Mute = _isMuted;
                    ApplyScaleMode();
                }
                finally
                {
                    mediaPlayer.Playing -= playingHandler;
                    mediaPlayer.EncounteredError -= errorHandler;
                }
            }
        }

        private void ApplyScaleMode()
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsPlaying)
            {
                return;
            }

            var ratio = DesktopHost.GetDisplayAspectRatio();
            _mediaPlayer.Scale = 0;
            _mediaPlayer.AspectRatio = _stretch == Stretch.Fill ? ratio : null;
            _mediaPlayer.CropGeometry = _stretch == Stretch.UniformToFill ? ratio : null;
        }

        private void DisposeWindow()
        {
            IsActive = false;
            IsPaused = false;

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
            }
            if (_media != null)
            {
                _media.Dispose();
            }
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Dispose();
            }
            if (_libVlc != null)
            {
                _libVlc.Dispose();
            }
            if (_window != null)
            {
                _window.Content = null;
                _window.Close();
            }

            _media = null;
            _mediaPlayer = null;
            _libVlc = null;
            _videoView = null;
            _window = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("WallpaperPlayer");
            }
        }
    }

    internal sealed class PlaybackFailedEventArgs : EventArgs
    {
        public PlaybackFailedEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }
}
