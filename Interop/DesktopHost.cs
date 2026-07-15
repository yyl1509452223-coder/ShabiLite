using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using ShabiLite.Services;

namespace ShabiLite.Interop
{
    internal static class DesktopHost
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const long WsChild = 0x40000000L;
        private const long WsVisible = 0x10000000L;
        private const long WsPopup = unchecked((long)0x80000000);
        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsSysMenu = 0x00080000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExNoActivate = 0x08000000L;
        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCxVirtualScreen = 78;
        private const int SmCyVirtualScreen = 79;
        private const uint ProgmanCreateWorkerW = 0x052C;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndBottom = new IntPtr(1);

        public static void AttachAsWallpaper(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                throw new InvalidOperationException("视频播放窗口尚未准备好。");
            }

            var programManager = FindWindow("Progman", null);
            if (programManager == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法找到 Windows 桌面管理窗口。");
            }

            PrepareWallpaperWindow(windowHandle);

            // Windows 11 24H2/25H2 may keep SHELLDLL_DefView under a WorkerW
            // instead of Progman. Ask Explorer to expose a wallpaper WorkerW,
            // then try every safe host before falling back to the older layout.
            RequestWallpaperWorker(programManager);
            Thread.Sleep(80);

            var iconLayer = FindDesktopIconLayer(programManager);
            var candidates = FindWallpaperHosts(programManager, iconLayer);
            Log.Write("桌面层探测：Progman=" + programManager
                + "，图标层=" + iconLayer.View
                + "，图标宿主=" + iconLayer.Host
                + "，候选宿主=" + candidates.Count + "。");

            var errors = new List<string>();
            foreach (var candidate in candidates)
            {
                int nativeError;
                if (TryAttach(windowHandle, candidate, out nativeError))
                {
                    Log.Write("桌面层挂载成功：方式=" + candidate.Name
                        + "，宿主=" + candidate.Host
                        + "，图标层=" + candidate.IconView + "。");
                    return;
                }

                var detail = candidate.Name + "(" + candidate.Host + ") Win32=" + nativeError;
                errors.Add(detail);
                Log.Write("桌面层挂载尝试失败：" + detail + "。");
            }

            var message = errors.Count == 0
                ? "没有找到可用的 Windows 桌面壁纸层。"
                : "所有桌面挂载方式均失败：" + string.Join("；", errors);
            throw new Win32Exception(Marshal.GetLastWin32Error(), message);
        }

        public static void RefreshDesktop()
        {
            var programManager = FindWindow("Progman", null);
            if (programManager != IntPtr.Zero)
            {
                RedrawWindow(programManager, IntPtr.Zero, IntPtr.Zero, 0x0085);
            }

            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x0085);
                }
                return true;
            }, IntPtr.Zero);
        }

        public static string GetDisplayAspectRatio()
        {
            var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
            var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
            var divisor = GreatestCommonDivisor(width, height);
            return (width / divisor) + ":" + (height / divisor);
        }

        private static void PrepareWallpaperWindow(IntPtr windowHandle)
        {
            var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
            style &= ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            SetWindowLongPtr(windowHandle, GwlStyle, new IntPtr(style | WsChild | WsVisible));

            var extendedStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(windowHandle, GwlExStyle,
                new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));
        }

        private static void RequestWallpaperWorker(IntPtr programManager)
        {
            IntPtr result;

            // Newer Windows 11 Explorer builds use the 0xD sequence.
            SendMessageTimeout(programManager, ProgmanCreateWorkerW,
                new IntPtr(0xD), new IntPtr(0x1), SmtoAbortIfHung, 1000, out result);
            SendMessageTimeout(programManager, ProgmanCreateWorkerW,
                new IntPtr(0xD), IntPtr.Zero, SmtoAbortIfHung, 1000, out result);

            // Older Windows 10/11 builds use the original zero-parameter message.
            SendMessageTimeout(programManager, ProgmanCreateWorkerW,
                IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 1000, out result);
        }

        private static DesktopIconLayer FindDesktopIconLayer(IntPtr programManager)
        {
            var directView = FindWindowEx(programManager, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (directView != IntPtr.Zero)
            {
                return new DesktopIconLayer(programManager, directView);
            }

            var result = new DesktopIconLayer(IntPtr.Zero, IntPtr.Zero);
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                var view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero)
                {
                    return true;
                }

                result = new DesktopIconLayer(window, view);
                return false;
            }, IntPtr.Zero);
            return result;
        }

        private static List<WallpaperHost> FindWallpaperHosts(
            IntPtr programManager,
            DesktopIconLayer iconLayer)
        {
            var results = new List<WallpaperHost>();

            // The blank WorkerW immediately after the icon host is the standard
            // wallpaper surface on modern Explorer builds.
            if (iconLayer.Host != IntPtr.Zero)
            {
                var workerAfterIcons = FindWindowEx(
                    IntPtr.Zero, iconLayer.Host, "WorkerW", null);
                AddCandidate(results, workerAfterIcons, IntPtr.Zero, "WorkerW-图标层后方");
            }

            // Explorer layouts vary, so also enumerate blank WorkerW windows.
            var worker = IntPtr.Zero;
            while (true)
            {
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (worker == IntPtr.Zero)
                {
                    break;
                }

                var containsIcons = FindWindowEx(
                    worker, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
                if (!containsIcons)
                {
                    AddCandidate(results, worker, IntPtr.Zero, "WorkerW-空白层");
                }
            }

            // If icons share a host with the wallpaper, position the video
            // directly behind SHELLDLL_DefView so desktop icons stay clickable.
            if (iconLayer.Host != IntPtr.Zero && iconLayer.View != IntPtr.Zero)
            {
                AddCandidate(results, iconLayer.Host, iconLayer.View, "图标宿主后方");
            }

            AddCandidate(results, programManager,
                iconLayer.Host == programManager ? iconLayer.View : IntPtr.Zero,
                "Progman兼容模式");
            return results;
        }

        private static void AddCandidate(
            List<WallpaperHost> candidates,
            IntPtr host,
            IntPtr iconView,
            string name)
        {
            if (host == IntPtr.Zero || !IsWindow(host))
            {
                return;
            }

            foreach (var existing in candidates)
            {
                if (existing.Host == host)
                {
                    return;
                }
            }

            candidates.Add(new WallpaperHost(host, iconView, name));
        }

        private static bool TryAttach(
            IntPtr windowHandle,
            WallpaperHost candidate,
            out int nativeError)
        {
            nativeError = 0;
            SetLastError(0);
            SetParent(windowHandle, candidate.Host);
            if (GetParent(windowHandle) != candidate.Host)
            {
                nativeError = Marshal.GetLastWin32Error();
                return false;
            }

            var virtualLeft = GetSystemMetrics(SmXVirtualScreen);
            var virtualTop = GetSystemMetrics(SmYVirtualScreen);
            var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
            var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
            RECT hostBounds;
            if (GetWindowRect(candidate.Host, out hostBounds))
            {
                virtualLeft -= hostBounds.Left;
                virtualTop -= hostBounds.Top;
            }

            var insertAfter = candidate.IconView != IntPtr.Zero
                ? candidate.IconView
                : HwndBottom;
            SetLastError(0);
            if (!SetWindowPos(windowHandle, insertAfter,
                    virtualLeft, virtualTop, width, height,
                    SwpNoActivate | SwpFrameChanged | SwpShowWindow))
            {
                nativeError = Marshal.GetLastWin32Error();
                return false;
            }

            return IsWindowVisible(windowHandle);
        }

        private static int GreatestCommonDivisor(int left, int right)
        {
            while (right != 0)
            {
                var next = left % right;
                left = right;
                right = next;
            }
            return Math.Abs(left);
        }

        private static IntPtr GetWindowLongPtr(IntPtr window, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(window, index)
                : new IntPtr(GetWindowLong32(window, index));
        }

        private static void SetWindowLongPtr(IntPtr window, int index, IntPtr value)
        {
            SetLastError(0);
            var result = IntPtr.Size == 8
                ? SetWindowLongPtr64(window, index, value)
                : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
            if (result == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private sealed class DesktopIconLayer
        {
            public DesktopIconLayer(IntPtr host, IntPtr view)
            {
                Host = host;
                View = view;
            }

            public IntPtr Host { get; private set; }
            public IntPtr View { get; private set; }
        }

        private sealed class WallpaperHost
        {
            public WallpaperHost(IntPtr host, IntPtr iconView, string name)
            {
                Host = host;
                IconView = iconView;
                Name = name;
            }

            public IntPtr Host { get; private set; }
            public IntPtr IconView { get; private set; }
            public string Name { get; private set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [DllImport("kernel32.dll")]
        private static extern void SetLastError(uint errorCode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowEx(
            IntPtr parent,
            IntPtr childAfter,
            string className,
            string windowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr window, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr child);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out RECT bounds);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(
            IntPtr window,
            IntPtr updateRect,
            IntPtr updateRegion,
            uint flags);
    }
}
