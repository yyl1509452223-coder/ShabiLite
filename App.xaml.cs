using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ShabiLite.Services;

namespace ShabiLite
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            Log.Write("应用启动。");
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\ShabiLite.VideoWallpaper", out createdNew);
            _ownsSingleInstanceMutex = createdNew;
            if (!createdNew)
            {
                MessageBox.Show("鲨壁已经在运行，请从右下角托盘打开。", "鲨壁", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Write("应用退出。");
            if (_singleInstanceMutex != null)
            {
                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                _singleInstanceMutex.Dispose();
            }

            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("未处理异常：" + e.Exception);
            MessageBox.Show(
                "鲨壁遇到错误：" + e.Exception.Message + "\n\n诊断日志已保存到：\n" + Log.Path,
                "鲨壁",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
