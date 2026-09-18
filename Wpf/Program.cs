using System;
using System.IO;
using System.Windows.Threading;
using System.Windows;

namespace KillWind.Wpf
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => WriteCrash(args.ExceptionObject as Exception);
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native", "MemoryBridge.exe");
            if (!File.Exists(helper))
            {
                MessageBox.Show("找不到 native\\MemoryBridge.exe。", "KillWind 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            application.DispatcherUnhandledException += (sender, args) => { WriteCrash(args.Exception); args.Handled = true; MessageBox.Show("发生未处理异常，详情已写入 CrashLogs。\n" + args.Exception.Message, "KillWind", MessageBoxButton.OK, MessageBoxImage.Error); };
            application.Run(new MainWindow(helper));
        }

        private static void WriteCrash(Exception error)
        {
            if (error == null) return;
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillWind", "CrashLogs");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".log");
                File.WriteAllText(file, DateTime.Now.ToString("o") + Environment.NewLine + error + Environment.NewLine + Environment.OSVersion);
            }
            catch (Exception writeError) { System.Diagnostics.Debug.WriteLine(writeError.Message); }
        }
    }
}
