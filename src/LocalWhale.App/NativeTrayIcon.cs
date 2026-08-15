using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LocalWhale.App;

internal sealed class NativeTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8051;
    private const uint NotifyAdd = 0;
    private const uint NotifyDelete = 2;
    private const uint NotifySetVersion = 4;
    private const uint NotifyMessage = 1;
    private const uint NotifyIcon = 2;
    private const uint NotifyTip = 4;
    private const uint NotifyVersion4 = 4;
    private const uint LeftButtonDoubleClick = 0x0203;
    private const uint RightButtonUp = 0x0205;
    private const int WindowProcIndex = -4;
    private const uint MenuString = 0;
    private const uint MenuSeparator = 0x0800;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackReturnCommand = 0x0100;
    private const uint CommandShow = 1;
    private const uint CommandRestart = 2;
    private const uint CommandExit = 3;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint LoadDefaultSize = 0x0040;

    private readonly IntPtr _window;
    private readonly Action _showWindow;
    private readonly Action _restartHarness;
    private readonly Action _exitApplication;
    private readonly IntPtr _icon;
    private readonly WindowProc _windowProc;
    private readonly IntPtr _previousWindowProc;
    private readonly uint _taskbarCreatedMessage;
    private bool _visible;
    private bool _disposed;

    public NativeTrayIcon(
        IntPtr window,
        string iconPath,
        Action showWindow,
        Action restartHarness,
        Action exitApplication)
    {
        _window = window;
        _showWindow = showWindow;
        _restartHarness = restartHarness;
        _exitApplication = exitApplication;
        _icon = LoadImage(
            IntPtr.Zero,
            Path.GetFullPath(iconPath),
            ImageIcon,
            0,
            0,
            LoadFromFile | LoadDefaultSize);
        if (_icon == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not load the LocalWhale tray icon.");
        }
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _windowProc = ProcessWindowMessage;
        _previousWindowProc = SetWindowLongPtr(
            window,
            WindowProcIndex,
            Marshal.GetFunctionPointerForDelegate(_windowProc));
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_visible) return;

        var data = CreateNotifyData();
        if (!ShellNotifyIcon(NotifyAdd, ref data))
        {
            throw new InvalidOperationException("Windows rejected the LocalWhale tray icon.");
        }

        data.VersionOrTimeout = NotifyVersion4;
        ShellNotifyIcon(NotifySetVersion, ref data);
        _visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_visible)
        {
            var data = CreateNotifyData();
            ShellNotifyIcon(NotifyDelete, ref data);
            _visible = false;
        }

        SetWindowLongPtr(_window, WindowProcIndex, _previousWindowProc);
        DestroyIcon(_icon);
    }

    private IntPtr ProcessWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _visible = false;
            Show();
            return IntPtr.Zero;
        }

        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64() & 0xFFFF);
            if (mouseMessage == LeftButtonDoubleClick)
            {
                _showWindow();
                return IntPtr.Zero;
            }

            if (mouseMessage == RightButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_previousWindowProc, window, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            AppendMenu(menu, MenuString, CommandShow, "显示 LocalWhale");
            AppendMenu(menu, MenuString, CommandRestart, "重启 Harness");
            AppendMenu(menu, MenuSeparator, 0, null);
            AppendMenu(menu, MenuString, CommandExit, "完全退出");
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(
                menu,
                TrackRightButton | TrackReturnCommand,
                point.X,
                point.Y,
                0,
                _window,
                IntPtr.Zero);
            switch (command)
            {
                case CommandShow:
                    _showWindow();
                    break;
                case CommandRestart:
                    _restartHarness();
                    break;
                case CommandExit:
                    _exitApplication();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NotifyIconData CreateNotifyData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = NotifyMessage | NotifyIcon | NotifyTip,
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = "LocalWhale"
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string? Info;
        public uint VersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string? InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW", SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint identifier, string? text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr window,
        IntPtr rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
