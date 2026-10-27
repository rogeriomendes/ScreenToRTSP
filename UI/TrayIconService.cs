using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreenToRTSP.UI
{
    public sealed class TrayIconService : IDisposable
    {
        private const int WM_DESTROY = 0x0002;
        private const int WM_COMMAND = 0x0111;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 20;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int NIN_BALLOONUSERCLICK = WM_USER + 5;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_INFO = 0x00000010;

        private const uint NIIF_INFO = 0x00000001;

        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        private const int CMD_START = 1001;
        private const int CMD_STOP = 1002;
        private const int CMD_WEB = 1003;
        private const int CMD_CONFIG = 1004;
        private const int CMD_EXIT = 1005;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractAssociatedIconW(IntPtr hInst, string lpIconPath, ref ushort lpiIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(
            uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
        private static extern void FlushMenuThemes();

        private IntPtr _hwnd;
        private IntPtr _hIcon;
        private NOTIFYICONDATA _nid;
        private readonly WndProcDelegate _wndProcDelegate;
        private bool _isAdded;
        private bool _disposed;

        public Func<bool> IsTransmittingProvider { get; set; }
        public Func<bool> IsWebServerEnabledProvider { get; set; }
        public Func<(string Start, string Stop, string OpenWeb, string Config, string Exit)> MenuLabelsProvider { get; set; }

        public event Action DoubleClicked;
        public event Action BalloonTipClicked;
        public event Action StartRequested;
        public event Action StopRequested;
        public event Action OpenWebRequested;
        public event Action OpenConfigRequested;
        public event Action ExitRequested;

        public TrayIconService()
        {
            try
            {
                SetPreferredAppMode(1); // AllowDark
                FlushMenuThemes();
            }
            catch { }

            _wndProcDelegate = WndProc;
            string className = "ScreenToRTSP_TrayWindow_" + Guid.NewGuid().ToString("N");
            IntPtr hInstance = GetModuleHandleW(null);

            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = hInstance,
                lpszClassName = className
            };

            RegisterClassExW(ref wndClass);
            _hwnd = CreateWindowExW(0, className, "ScreenToRTSP_Tray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
            if (File.Exists(icoPath))
            {
                _hIcon = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            }
            if (_hIcon == IntPtr.Zero)
            {
                string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenToRTSP.exe");
                ushort idx = 0;
                _hIcon = ExtractAssociatedIconW(hInstance, exePath, ref idx);
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Screen To RTSP",
                szInfo = "",
                szInfoTitle = ""
            };

            _isAdded = Shell_NotifyIconW(NIM_ADD, ref _nid);
        }

        public bool Visible
        {
            get => _isAdded;
            set
            {
                if (value && !_isAdded)
                {
                    _isAdded = Shell_NotifyIconW(NIM_ADD, ref _nid);
                }
                else if (!value && _isAdded)
                {
                    Shell_NotifyIconW(NIM_DELETE, ref _nid);
                    _isAdded = false;
                }
            }
        }

        public void SetTooltip(string tip)
        {
            if (_disposed) return;
            _nid.szTip = tip?.Length > 120 ? tip.Substring(0, 120) : (tip ?? "Screen To RTSP");
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            if (_isAdded)
            {
                Shell_NotifyIconW(NIM_MODIFY, ref _nid);
            }
        }

        public void ShowBalloonTip(string title, string message)
        {
            if (_disposed) return;
            if (!_isAdded) Visible = true;

            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO;
            _nid.szInfoTitle = title?.Length > 60 ? title.Substring(0, 60) : (title ?? "");
            _nid.szInfo = message?.Length > 250 ? message.Substring(0, 250) : (message ?? "");
            _nid.dwInfoFlags = NIIF_INFO;
            _nid.uTimeoutOrVersion = 10000;
            Shell_NotifyIconW(NIM_MODIFY, ref _nid);
        }

        private void ShowContextMenu()
        {
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                bool isTransmitting = IsTransmittingProvider?.Invoke() ?? false;
                bool isWebEnabled = IsWebServerEnabledProvider?.Invoke() ?? false;
                var labels = MenuLabelsProvider?.Invoke() ?? ("▶ Iniciar", "■ Parar", "Abrir Painel Web", "Abrir Configuração", "Sair");

                if (!isTransmitting)
                {
                    AppendMenuW(hMenu, MF_STRING, (UIntPtr)CMD_START, labels.Start);
                }
                else
                {
                    AppendMenuW(hMenu, MF_STRING, (UIntPtr)CMD_STOP, labels.Stop);
                }

                AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);

                if (isWebEnabled)
                {
                    AppendMenuW(hMenu, MF_STRING, (UIntPtr)CMD_WEB, labels.OpenWeb);
                }

                AppendMenuW(hMenu, MF_STRING, (UIntPtr)CMD_CONFIG, labels.Config);
                AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
                AppendMenuW(hMenu, MF_STRING, (UIntPtr)CMD_EXIT, labels.Exit);

                GetCursorPos(out POINT pt);
                SetForegroundWindow(_hwnd);
                int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, _hwnd, IntPtr.Zero);

                switch (cmd)
                {
                    case CMD_START:
                        StartRequested?.Invoke();
                        break;
                    case CMD_STOP:
                        StopRequested?.Invoke();
                        break;
                    case CMD_WEB:
                        OpenWebRequested?.Invoke();
                        break;
                    case CMD_CONFIG:
                        OpenConfigRequested?.Invoke();
                        break;
                    case CMD_EXIT:
                        ExitRequested?.Invoke();
                        break;
                }
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYICON)
            {
                int eventId = (int)(lParam.ToInt64() & 0xFFFF);
                if (eventId == WM_LBUTTONDBLCLK)
                {
                    DoubleClicked?.Invoke();
                    return IntPtr.Zero;
                }
                else if (eventId == WM_RBUTTONUP || eventId == WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                    return IntPtr.Zero;
                }
                else if (eventId == NIN_BALLOONUSERCLICK)
                {
                    BalloonTipClicked?.Invoke();
                    return IntPtr.Zero;
                }
            }
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_isAdded)
            {
                Shell_NotifyIconW(NIM_DELETE, ref _nid);
                _isAdded = false;
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }
}
