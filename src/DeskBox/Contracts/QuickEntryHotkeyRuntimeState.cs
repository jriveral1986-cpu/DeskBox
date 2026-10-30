using DeskBox.Models;

namespace DeskBox.Contracts;

public sealed record QuickEntryHotkeyRuntimeState(
    bool Enabled, bool IsRegistered, GlobalHotkeyGesture Gesture, string? Error);
