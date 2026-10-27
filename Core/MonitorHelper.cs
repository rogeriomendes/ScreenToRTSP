using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;

namespace ScreenToRTSP.Core
{
    public static class MonitorHelper
    {
        private static string _cachedAppDir;
        public static string AppDir => _cachedAppDir ??= (Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory);

        private static List<Rectangle> _cachedMonitors;
        private static long _cachedMonitorsTick;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static List<Rectangle> GetMonitors(bool forceRefresh = false)
        {
            long now = Environment.TickCount64;
            if (!forceRefresh && _cachedMonitors != null && (now - _cachedMonitorsTick) < 5000)
            {
                return _cachedMonitors;
            }

            var monitors = new List<Rectangle>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                monitors.Add(new Rectangle(
                    lprcMonitor.Left,
                    lprcMonitor.Top,
                    lprcMonitor.Right - lprcMonitor.Left,
                    lprcMonitor.Bottom - lprcMonitor.Top));
                return true;
            }, IntPtr.Zero);

            _cachedMonitors = monitors;
            _cachedMonitorsTick = now;
            return monitors;
        }

        public static void TrimProcessMemory()
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
                using var proc = Process.GetCurrentProcess();
                EmptyWorkingSet(proc.Handle);
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        public const uint MB_OK = 0x00000000;
        public const uint MB_OKCANCEL = 0x00000001;
        public const uint MB_ICONERROR = 0x00000010;
        public const uint MB_ICONWARNING = 0x00000030;
        public const uint MB_ICONINFORMATION = 0x00000040;
        public const int IDOK = 1;

        public static void ShowMessage(string text, string title = "Screen To RTSP", uint icon = MB_ICONINFORMATION, IntPtr hwnd = default)
        {
            MessageBoxW(hwnd, text, title, MB_OK | icon);
        }

        public static bool ShowConfirm(string text, string title = "Screen To RTSP", IntPtr hwnd = default)
        {
            return MessageBoxW(hwnd, text, title, MB_OKCANCEL | MB_ICONWARNING) == IDOK;
        }
    }
}
