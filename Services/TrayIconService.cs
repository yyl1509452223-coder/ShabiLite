using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ShabiLite.Services
{
    internal sealed class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _togglePlaybackItem;
        private readonly Icon _icon;
        private bool _noticeShown;

        public TrayIconService()
        {
            var executablePath = Process.GetCurrentProcess().MainModule.FileName;
            _icon = Icon.ExtractAssociatedIcon(executablePath) ?? SystemIcons.Application;
            _menu = new ContextMenuStrip();
            var showItem = new ToolStripMenuItem("显示鲨壁");
            _togglePlaybackItem = new ToolStripMenuItem("暂停播放") { Enabled = false };
            var exitItem = new ToolStripMenuItem("退出鲨壁");

            showItem.Font = new Font(showItem.Font, FontStyle.Bold);
            showItem.Click += delegate { Raise(RestoreRequested); };
            _togglePlaybackItem.Click += delegate { Raise(TogglePlaybackRequested); };
            exitItem.Click += delegate { Raise(ExitRequested); };
            _menu.Items.Add(showItem);
            _menu.Items.Add(_togglePlaybackItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = _icon,
                Text = "鲨壁",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += delegate { Raise(RestoreRequested); };
        }

        public event EventHandler RestoreRequested;
        public event EventHandler TogglePlaybackRequested;
        public event EventHandler ExitRequested;

        public void SetPlaybackAvailable(bool available)
        {
            _togglePlaybackItem.Enabled = available;
        }

        public void SetPaused(bool paused)
        {
            _togglePlaybackItem.Text = paused ? "继续播放" : "暂停播放";
        }

        public void ShowMinimizedNotice()
        {
            if (_noticeShown)
            {
                return;
            }

            _noticeShown = true;
            _notifyIcon.ShowBalloonTip(
                2200,
                "鲨壁仍在运行",
                "双击托盘图标可重新打开控制面板。",
                ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            if (!ReferenceEquals(_icon, SystemIcons.Application))
            {
                _icon.Dispose();
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
