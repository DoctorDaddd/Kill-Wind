using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KillWind.Wpf
{
    internal sealed class GlobalHotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NONE = 0;
        private readonly Window window;
        private readonly Action<int> callback;
        private readonly List<int> registered = new List<int>();
        private HwndSource source;

        public GlobalHotkeyService(Window window, Action<int> callback)
        {
            this.window = window; this.callback = callback;
        }

        public void RegisterDefaults()
        {
            if (source != null) return;
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source == null) return;
            source.AddHook(WindowProc);
            for (int index = 0; index < 8; index++)
            {
                int id = 0x4B00 + index;
                if (NativeMethods.RegisterHotKey(source.Handle, id, MOD_NONE, (uint)(0x70 + index))) registered.Add(id);
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                int index = registered.IndexOf(id);
                if (index >= 0) { callback(index); handled = true; }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (source == null) return;
            foreach (int id in registered) NativeMethods.UnregisterHotKey(source.Handle, id);
            source.RemoveHook(WindowProc); registered.Clear(); source = null;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }
}
