using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace NovaLite.UI.Services;

/// <summary>Shows a Windows notification-center alert through the shell.</summary>
public sealed class WindowsToastService
{
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifInfo = 16;
    private const uint NiifInfo = 1;
    private const int IdiInformation = 32516;

    private readonly Window _window;
    private bool _isRegistered;

    public WindowsToastService(Window window) => _window = window;

    public void Show(string title, string message)
    {
        if (!OperatingSystem.IsWindows()) return;

        IntPtr windowHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (windowHandle == IntPtr.Zero) return;

        var data = CreateData(windowHandle);
        if (!_isRegistered)
        {
            ShellNotifyIcon(NimAdd, ref data);
            _isRegistered = true;
        }

        data.uFlags = NifInfo;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = NiifInfo;
        ShellNotifyIcon(NimModify, ref data);
    }

    private static NotifyIconData CreateData(IntPtr windowHandle) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = windowHandle,
        uID = 1,
        uFlags = NifIcon | NifTip,
        hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IdiInformation),
        szTip = "NovaLite",
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
