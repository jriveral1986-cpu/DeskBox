using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryReverseEnableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", "QuickEntryReverseEnable", Guid.NewGuid().ToString("N"));

    [Fact]
    public Task ReenableConflictingVisibilityShortcut_RetainsCaptureOwnershipAndDisabledPreference() =>
        AssertReverseEnableAsync(visibility: true);

    [Fact]
    public Task ReenableConflictingSearchShortcut_RetainsCaptureOwnershipAndDisabledPreference() =>
        AssertReverseEnableAsync(visibility: false);

    private async Task AssertReverseEnableAsync(bool visibility)
    {
        var settings = new SettingsService(_root);
        settings.Settings.Core.GlobalHotkeyEnabled = false;
        settings.Settings.Core.GlobalHotkeyActivationKind = HotkeyActivationKind.Chord;
        settings.Settings.Core.GlobalHotkeyModifiers = 3;
        settings.Settings.Core.GlobalHotkeyKey = 0x4E;
        settings.Settings.Search.SearchHotkeyEnabled = false;
        settings.Settings.Search.SearchHotkeyModifiers = 3;
        settings.Settings.Search.SearchHotkeyKey = 0x4E;
        var localization = new LocalizationService(settings);
        var backend = new CaptureBackend();
        using var capture = new QuickEntryHotkeyService(settings, localization, () => Task.CompletedTask,
            backend, () => Task.FromResult(true));
        using var global = new GlobalHotkeyService(settings, localization, () => Task.CompletedTask);
        using var search = new SearchHotkeyService(settings, localization, () => Task.CompletedTask);
        capture.Attach(new IntPtr(1));
        Assert.True((await capture.SetEnabledAsync(true)).IsRegistered);
        Assert.Equal(QuickEntryPolicy.DefaultGesture, backend.Registered);
        await settings.SaveAsync();

        // The competing services are real, but have no native HWND attached.
        // Their enable paths still own the persisted preference transition.
        if (visibility) global.SetEnabled(true); else search.SetEnabled(true);
        Assert.True(await settings.FlushPendingSaveAsync());
        capture.RefreshRegistration();

        bool competingEnabled = visibility ? settings.Settings.Core.GlobalHotkeyEnabled : settings.Settings.Search.SearchHotkeyEnabled;
        Assert.False(competingEnabled);
        Assert.True(capture.IsRegistered);
        Assert.Equal(QuickEntryPolicy.DefaultGesture, backend.Registered);
        using JsonDocument persisted = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
        Assert.False(persisted.RootElement.GetProperty(visibility ? "globalHotkeyEnabled" : "searchHotkeyEnabled").GetBoolean());
        Assert.True(persisted.RootElement.GetProperty("quickEntryHotkeyEnabled").GetBoolean());
    }

    private sealed class CaptureBackend : IQuickEntryHotkeyBackend
    {
        public GlobalHotkeyGesture? Registered { get; private set; }
        public bool Attach(IntPtr window, Action invoked) => true;
        public bool Register(IntPtr window, GlobalHotkeyGesture gesture) { Registered = gesture; return true; }
        public void Unregister(IntPtr window) => Registered = null;
        public void Detach(IntPtr window) { }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
