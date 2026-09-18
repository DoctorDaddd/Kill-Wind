using System;
using System.IO;
using System.Windows;

namespace KillWind.Wpf
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native", "MemoryBridge.exe");
            if (!File.Exists(helper))
            {
                MessageBox.Show("找不到 native\\MemoryBridge.exe。", "KillWind 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            application.Run(new MainWindow(helper));
        }
    }
}
