using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Pocketknife.Core.Launcher;

namespace Pocketknife.App.Services;

/// <summary>A system-wide hotkey registered with RegisterHotKey on a message-only window.</summary>
public sealed partial class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5442;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _window;
    private readonly Action _pressed;
    private bool _registered;

    /// <summary>The message-only window that receives WM_HOTKEY.</summary>
    internal IntPtr Handle => _window.Handle;

    public HotkeyService(Action pressed)
    {
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("PocketknifeHotkey") { ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
    }

    /// <summary>Registers <paramref name="gesture"/>, replacing any previous one. False if Windows refused it.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        if (gesture.Key.All(char.IsAsciiDigit) || !Enum.TryParse<Key>(gesture.Key, ignoreCase: true, out var key) || key == Key.None)
        {
            return false;
        }

        _registered = RegisterHotKey(_window.Handle, HotkeyId, (uint)gesture.Modifiers | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _pressed();
        }

        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);
}
