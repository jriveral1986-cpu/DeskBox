using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>Only ordinary application chords are eligible for quick entry.</summary>
public static class QuickEntryPolicy
{
    public static GlobalHotkeyGesture DefaultGesture => new(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt, 0x4E);

    public static bool IsAllowedGesture(GlobalHotkeyGesture gesture)
    {
        const HotkeyModifierKeys allowed = HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt | HotkeyModifierKeys.Shift;
        if ((gesture.Modifiers & ~allowed) != 0 ||
            (gesture.Modifiers & (HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt)) == 0 ||
            !GlobalHotkeyService.IsValidGesture(gesture) || GlobalHotkeyService.IsReservedSystemGesture(gesture)) return false;
        if (gesture.Modifiers.HasFlag(HotkeyModifierKeys.Alt) && gesture.VirtualKey is 0x09 or 0x1B or 0x73) return false;
        if (gesture.Modifiers.HasFlag(HotkeyModifierKeys.Control) && gesture.VirtualKey == 0x1B) return false;
        return !(gesture.Modifiers.HasFlag(HotkeyModifierKeys.Control) &&
            gesture.Modifiers.HasFlag(HotkeyModifierKeys.Alt) && gesture.VirtualKey == 0x2E);
    }

    public static bool IsOwnedByCapture(AppSettings settings, GlobalHotkeyGesture gesture) =>
        settings.QuickCapture.QuickEntryHotkeyEnabled &&
        gesture == new GlobalHotkeyGesture((HotkeyModifierKeys)settings.QuickCapture.QuickEntryHotkeyModifiers,
            settings.QuickCapture.QuickEntryHotkeyKey);

    public static bool ConflictsWithExisting(AppSettings settings, GlobalHotkeyGesture gesture) =>
        (settings.Core.GlobalHotkeyEnabled && settings.Core.GlobalHotkeyActivationKind == HotkeyActivationKind.Chord &&
            gesture == new GlobalHotkeyGesture((HotkeyModifierKeys)settings.Core.GlobalHotkeyModifiers, settings.Core.GlobalHotkeyKey)) ||
        (settings.Search.SearchHotkeyEnabled && gesture == new GlobalHotkeyGesture(
            (HotkeyModifierKeys)settings.Search.SearchHotkeyModifiers, settings.Search.SearchHotkeyKey));

    public static bool NormalizeSettings(QuickCaptureSettingsSlice slice)
    {
        bool changed = false;
        if (!IsAllowedGesture(new GlobalHotkeyGesture((HotkeyModifierKeys)slice.QuickEntryHotkeyModifiers, slice.QuickEntryHotkeyKey)))
        {
            slice.QuickEntryHotkeyEnabled = false;
            slice.QuickEntryHotkeyModifiers = (int)DefaultGesture.Modifiers;
            slice.QuickEntryHotkeyKey = DefaultGesture.VirtualKey;
            changed = true;
        }
        if (slice.QuickEntryLastTodoWidgetId is null) { slice.QuickEntryLastTodoWidgetId = string.Empty; changed = true; }
        if (slice.QuickEntryLastKind is not ("Task" or "Note")) { slice.QuickEntryLastKind = "Note"; changed = true; }
        return changed;
    }
}

public sealed class Migration_15_To_16 : ISettingsMigration
{
    public int FromVersion => 15;
    public void Migrate(AppSettings settings)
    {
        // Absent fields already use opt-in defaults. Preserve explicit settings
        // from this fork; never activate a global shortcut during migration.
        QuickEntryPolicy.NormalizeSettings(settings.QuickCapture);
    }
}
