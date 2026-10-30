using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryHotkeyTests
{
    private static (SettingsService Settings, Backend Backend, QuickEntryHotkeyService Service) Create(Func<Task<bool>>? persist = null)
    {
        var settings = new SettingsService();
        var backend = new Backend();
        var service = new QuickEntryHotkeyService(settings, new LocalizationService(settings),
            () => Task.CompletedTask, backend, persist ?? (() => Task.FromResult(true)));
        service.Attach(new IntPtr(1));
        return (settings, backend, service);
    }

    [Fact]
    public void OptInDefault_DoesNotRegisterUntilEnabled()
    {
        var (settings, backend, service) = Create();
        using (service)
        {
            Assert.False(settings.Settings.QuickCapture.QuickEntryHotkeyEnabled);
            Assert.Null(backend.Registered);
            Assert.False(service.IsRegistered);
        }
    }

    [Fact]
    public async Task FailedRemap_RestoresPreviousRegistrationAndSettings()
    {
        var (settings, backend, service) = Create();
        using (service)
        {
            await service.SetEnabledAsync(true);
            var occupied = new GlobalHotkeyGesture(HotkeyModifierKeys.Control | HotkeyModifierKeys.Shift, 0x51);
            backend.Denied.Add(occupied);
            var result = await service.ApplyAsync(occupied);
            Assert.NotNull(result.Error);
            Assert.True(result.Enabled);
            Assert.True(service.IsRegistered);
            Assert.Equal(QuickEntryPolicy.DefaultGesture, backend.Registered);
            Assert.Equal(0x4E, settings.Settings.QuickCapture.QuickEntryHotkeyKey);
        }
    }

    [Fact]
    public async Task FailedPersistence_DoesNotEnableOrRetainProbedShortcut()
    {
        var (settings, backend, service) = Create(() => Task.FromResult(false));
        using (service)
        {
            var result = await service.SetEnabledAsync(true);
            Assert.NotNull(result.Error);
            Assert.False(result.Enabled);
            Assert.False(settings.Settings.QuickCapture.QuickEntryHotkeyEnabled);
            Assert.Null(backend.Registered);
        }
    }

    [Fact]
    public async Task ConflictWithMain_DoesNotChangeEnabledState()
    {
        var (settings, backend, service) = Create();
        using (service)
        {
            settings.Settings.Core.GlobalHotkeyModifiers = 3;
            settings.Settings.Core.GlobalHotkeyKey = 0x4E;
            var result = await service.SetEnabledAsync(true);
            Assert.NotNull(result.Error);
            Assert.False(result.Enabled);
            Assert.Null(backend.Registered);
        }
    }

    [Fact]
    public async Task RecorderSuspension_ProbesNewGestureThenRestoresRegistrationOnResume()
    {
        var (_, backend, service) = Create();
        using (service)
        {
            await service.SetEnabledAsync(true);
            service.SuspendForRecording();
            Assert.Null(backend.Registered);
            var requested = new GlobalHotkeyGesture(HotkeyModifierKeys.Control | HotkeyModifierKeys.Shift, 0x51);
            var result = await service.ApplyAsync(requested);
            Assert.Null(result.Error);
            Assert.True(result.Enabled);
            Assert.Null(backend.Registered);
            service.ResumeAfterRecording();
            Assert.Equal(requested, backend.Registered);
        }
        Assert.Null(backend.Registered);
        Assert.Equal(1, backend.DetachCount);
    }

    [Fact]
    public async Task ReservedGesture_IsRejectedWithoutRetiringCurrentShortcut()
    {
        var (_, backend, service) = Create();
        using (service)
        {
            await service.SetEnabledAsync(true);
            var result = await service.ApplyAsync(new(HotkeyModifierKeys.Alt, 0x20));
            Assert.NotNull(result.Error);
            Assert.Equal(QuickEntryPolicy.DefaultGesture, backend.Registered);
        }
    }

    private sealed class Backend : IQuickEntryHotkeyBackend
    {
        public GlobalHotkeyGesture? Registered { get; private set; }
        public HashSet<GlobalHotkeyGesture> Denied { get; } = [];
        public int DetachCount { get; private set; }
        public bool Attach(IntPtr window, Action invoked) => true;
        public bool Register(IntPtr window, GlobalHotkeyGesture gesture)
        {
            if (Denied.Contains(gesture)) return false;
            Registered = gesture; return true;
        }
        public void Unregister(IntPtr window) => Registered = null;
        public void Detach(IntPtr window) => DetachCount++;
    }
}
