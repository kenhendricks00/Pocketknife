using Pocketknife.Core.Launcher;

namespace Pocketknife.App.Services;

/// <summary>The saved launcher shortcut and whether Windows let Pocketknife register it.</summary>
public sealed class LauncherHotkey(HotkeyService service, AppServices services)
{
    public HotkeyGesture Current =>
        HotkeyGesture.TryParse(services.Settings.LauncherHotkey, out var gesture) ? gesture! : Default;

    public static HotkeyGesture Default { get; } = HotkeyGesture.TryParse(HotkeyGesture.Default, out var gesture) ? gesture! : throw new InvalidOperationException();

    public bool IsRegistered { get; private set; }

    public bool Register()
    {
        IsRegistered = service.Register(Current);
        return IsRegistered;
    }

    /// <summary>Releases the shortcut while a new one is being typed, so pressing it doesn't open the launcher.</summary>
    public void Suspend()
    {
        service.Unregister();
        IsRegistered = false;
    }

    /// <summary>Registers and saves <paramref name="gesture"/>. If Windows refuses it, the old shortcut stays and the reason is returned.</summary>
    public string? TryChange(HotkeyGesture gesture)
    {
        if (!service.Register(gesture))
        {
            var restored = Register();
            return restored
                ? $"{gesture} is already used by another app, so {Current} is still the launcher shortcut."
                : $"{gesture} is already used by another app. {Current} isn't available either; open the launcher from the notification-area icon.";
        }

        IsRegistered = true;
        services.Update(s => s with { LauncherHotkey = gesture.ToString() });
        return null;
    }
}
