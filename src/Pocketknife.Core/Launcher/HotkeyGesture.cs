namespace Pocketknife.Core.Launcher;

/// <summary>Modifier flags, with the same values as the Win32 RegisterHotKey MOD_ constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global shortcut such as Ctrl+Alt+Space. <see cref="Key"/> is a WPF key name, for example Space, K, D1 or F5.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public const string Default = "Ctrl+Alt+Space";

    private static readonly HashSet<string> s_modifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LeftShift", "RightShift", "LWin", "RWin", "System", "None",
    };

    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Ctrl; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null)
                    {
                        return false;
                    }

                    key = part.Length == 1 && char.IsAsciiDigit(part[0]) ? "D" + part : part;
                    break;
            }
        }

        if (key is null || s_modifierKeys.Contains(key) || !key.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, char.ToUpperInvariant(key[0]) + key[1..]);
        return true;
    }

    public static bool IsModifierKey(string key) => s_modifierKeys.Contains(key);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(Key.Length == 2 && Key[0] == 'D' && char.IsAsciiDigit(Key[1]) ? Key[1..] : Key);
        return string.Join('+', parts);
    }
}

public enum HotkeyVerdictLevel
{
    Ok,
    Warn,
    Reject,
}

public sealed record HotkeyVerdict(HotkeyVerdictLevel Level, string Message);

/// <summary>Spots launcher shortcuts that would get in the way of typing, Windows, or common tools.</summary>
public static class HotkeyAdvice
{
    private static readonly Dictionary<string, string> s_reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt+Tab"] = "Windows uses Alt+Tab to switch apps.",
        ["Alt+F4"] = "Alt+F4 closes the current window.",
        ["Alt+Escape"] = "Windows uses Alt+Esc to cycle windows.",
        ["Ctrl+Escape"] = "Ctrl+Esc opens the Start menu.",
        ["Ctrl+Shift+Escape"] = "Ctrl+Shift+Esc opens Task Manager.",
        ["Ctrl+Alt+Delete"] = "Ctrl+Alt+Delete is reserved by Windows.",
        ["Win+L"] = "Win+L locks the PC.",
        ["Win+D"] = "Win+D shows the desktop.",
        ["Win+E"] = "Win+E opens File Explorer.",
        ["Win+R"] = "Win+R opens the Run box.",
        ["Win+S"] = "Win+S opens Windows Search.",
        ["Win+I"] = "Win+I opens Windows Settings.",
        ["Win+V"] = "Win+V opens clipboard history.",
        ["Win+X"] = "Win+X opens the quick link menu.",
        ["Win+Tab"] = "Win+Tab opens Task View.",
    };

    private static readonly Dictionary<string, string> s_conflicts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt+Space"] = "Alt+Space opens the window menu in every app, and PowerToys Run uses it by default.",
        ["Win+Space"] = "Win+Space switches the keyboard layout.",
        ["Ctrl+Space"] = "Ctrl+Space switches input methods and opens suggestions in many editors.",
        ["Win+Alt+Space"] = "PowerToys Command Palette uses Win+Alt+Space by default.",
        ["Ctrl+Shift+Space"] = "Many editors use Ctrl+Shift+Space to show parameter hints.",
    };

    public static HotkeyVerdict Check(HotkeyGesture gesture)
    {
        var name = gesture.ToString();
        if ((gesture.Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
        {
            return new(HotkeyVerdictLevel.Reject, "Include Ctrl, Alt or Win so the shortcut doesn't get in the way of typing.");
        }

        if (s_reserved.TryGetValue(name, out var reserved))
        {
            return new(HotkeyVerdictLevel.Reject, reserved);
        }

        if (s_conflicts.TryGetValue(name, out var conflict))
        {
            return new(HotkeyVerdictLevel.Warn, conflict);
        }

        return gesture.Modifiers.HasFlag(HotkeyModifiers.Win)
            ? new(HotkeyVerdictLevel.Warn, "Windows keeps many Win shortcuts for itself, so this one may not work everywhere.")
            : new(HotkeyVerdictLevel.Ok, "");
    }
}
