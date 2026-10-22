using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Toolbox.App.Services;

/// <summary>A system-wide hotkey registered with RegisterHotKey on a message-only window.</summary>
public sealed partial class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5442;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _window;
    private readonly Action _pressed;
    private bool _registered;

    public HotkeyService(Action pressed)
    {
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("WindowsToolboxHotkey") { ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
    }

    public static IReadOnlyList<string> Choices { get; } = ["Ctrl+Alt+Space", "Ctrl+Shift+Space", "Alt+Shift+Space", "Ctrl+Alt+T", "Ctrl+Shift+K"];

    /// <summary>Registers <paramref name="gesture"/> (for example "Ctrl+Alt+Space"), replacing any previous one.</summary>
    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParse(gesture, out var modifiers, out var key))
        {
            return false;
        }

        _registered = RegisterHotKey(_window.Handle, HotkeyId, modifiers | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    internal static bool TryParse(string gesture, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        foreach (var part in gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "ALT": modifiers |= 0x1; break;
                case "CTRL": modifiers |= 0x2; break;
                case "SHIFT": modifiers |= 0x4; break;
                case "WIN": modifiers |= 0x8; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        return key != Key.None && modifiers != 0;
    }

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
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
