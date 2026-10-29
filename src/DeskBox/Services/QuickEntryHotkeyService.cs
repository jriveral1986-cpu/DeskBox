using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Services;

internal interface IQuickEntryHotkeyBackend
{
    bool Attach(IntPtr window, Action invoked);
    bool Register(IntPtr window, GlobalHotkeyGesture gesture);
    void Unregister(IntPtr window);
    void Detach(IntPtr window);
}

public sealed class QuickEntryHotkeyService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly Func<Task> _invoke;
    private readonly IQuickEntryHotkeyBackend _backend;
    private readonly Func<Task<bool>> _persist;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private IntPtr _window;
    private bool _registered;
    private bool _suspended;
    private bool _disposed;
    private string? _error;

    public QuickEntryHotkeyService(SettingsService settings, LocalizationService localization, Func<Task> invoke)
        : this(settings, localization, invoke, new WindowsQuickEntryHotkeyBackend(), () => settings.SaveCheckedAsync()) { }

    internal QuickEntryHotkeyService(SettingsService settings, LocalizationService localization, Func<Task> invoke,
        IQuickEntryHotkeyBackend backend, Func<Task<bool>> persist)
    {
        _settings = settings; _localization = localization; _invoke = invoke; _backend = backend; _persist = persist;
    }

    public GlobalHotkeyGesture Gesture => new((HotkeyModifierKeys)_settings.Settings.QuickCapture.QuickEntryHotkeyModifiers,
        _settings.Settings.QuickCapture.QuickEntryHotkeyKey);
    public bool IsRegistered => _registered;
    public QuickEntryHotkeyRuntimeState State => new(_settings.Settings.QuickCapture.QuickEntryHotkeyEnabled,
        _registered, Gesture, _error);

    public void Attach(IntPtr window)
    {
        if (_disposed || window == IntPtr.Zero) return;
        if (_window != IntPtr.Zero) { _backend.Unregister(_window); _backend.Detach(_window); }
        _window = window;
        if (!_backend.Attach(window, OnInvoked))
        { _window = IntPtr.Zero; _error = _localization.T("QuickEntry.ShortcutFailed"); return; }
        RefreshRegistration();
    }

    public void RefreshRegistration()
    {
        if (_disposed || _window == IntPtr.Zero) return;
        _backend.Unregister(_window); _registered = false;
        if (_suspended || !_settings.Settings.QuickCapture.QuickEntryHotkeyEnabled) return;
        if (!QuickEntryPolicy.IsAllowedGesture(Gesture) || QuickEntryPolicy.ConflictsWithExisting(_settings.Settings, Gesture))
        { _error = _localization.T("QuickEntry.ShortcutConflict"); return; }
        _registered = _backend.Register(_window, Gesture);
        _error = _registered ? null : _localization.T("QuickEntry.ShortcutFailed");
    }

    public Task<QuickEntryHotkeyRuntimeState> SetEnabledAsync(bool enabled) => ApplyAsync(Gesture, enabled);

    public async Task<QuickEntryHotkeyRuntimeState> ApplyAsync(GlobalHotkeyGesture gesture, bool? enabled = null)
    {
        await _applyGate.WaitAsync();
        try
        {
            if (_disposed || _window == IntPtr.Zero)
            { _error = _localization.T("QuickEntry.ShortcutFailed"); return State; }
            var slice = _settings.Settings.QuickCapture;
            bool nextEnabled = enabled ?? slice.QuickEntryHotkeyEnabled;
            if (!QuickEntryPolicy.IsAllowedGesture(gesture))
            { _error = _localization.T("QuickEntry.ShortcutInvalid"); return State; }
            if (nextEnabled && QuickEntryPolicy.ConflictsWithExisting(_settings.Settings, gesture))
            { _error = _localization.T("QuickEntry.ShortcutConflict"); return State; }

            GlobalHotkeyGesture previous = Gesture;
            bool previousEnabled = slice.QuickEntryHotkeyEnabled;
            _backend.Unregister(_window); _registered = false;
            // Probe the actual OS registration even while the recorder suspends
            // dispatch. A saved gesture must not silently claim an occupied key.
            bool probe = !nextEnabled || _backend.Register(_window, gesture);
            if (!probe)
            {
                RestoreRegistration(previous, previousEnabled);
                _error = _localization.T("QuickEntry.ShortcutConflict");
                return State;
            }
            slice.QuickEntryHotkeyModifiers = (int)gesture.Modifiers;
            slice.QuickEntryHotkeyKey = gesture.VirtualKey;
            slice.QuickEntryHotkeyEnabled = nextEnabled;
            bool saved;
            try { saved = await _persist(); }
            catch (Exception ex) { App.Log($"[QuickEntry] Shortcut save failed: {ex.Message}"); saved = false; }
            if (!saved)
            {
                slice.QuickEntryHotkeyModifiers = (int)previous.Modifiers;
                slice.QuickEntryHotkeyKey = previous.VirtualKey;
                slice.QuickEntryHotkeyEnabled = previousEnabled;
                RestoreRegistration(previous, previousEnabled);
                _error = _localization.T("QuickEntry.SaveFailed");
                return State;
            }
            _registered = nextEnabled;
            if (_suspended) { _backend.Unregister(_window); _registered = false; }
            _error = null;
            return State;
        }
        finally { _applyGate.Release(); }
    }

    private void RestoreRegistration(GlobalHotkeyGesture gesture, bool enabled)
    {
        _backend.Unregister(_window);
        _registered = enabled && !_suspended && _backend.Register(_window, gesture);
    }

    public void SuspendForRecording()
    {
        _suspended = true;
        if (_window != IntPtr.Zero) _backend.Unregister(_window);
        _registered = false;
    }

    public void ResumeAfterRecording() { _suspended = false; RefreshRegistration(); }

    private async void OnInvoked()
    {
        if (_disposed || _suspended || !_registered) return;
        try { await _invoke(); }
        catch (Exception ex) { App.Log($"[QuickEntry] Invocation failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_window != IntPtr.Zero) { _backend.Unregister(_window); _backend.Detach(_window); }
        _window = IntPtr.Zero; _registered = false;
    }
}

internal sealed class WindowsQuickEntryHotkeyBackend : IQuickEntryHotkeyBackend
{
    private const int HotkeyId = 0x454E;
    private static readonly UIntPtr SubclassId = new(0x454E);
    private readonly Win32Helper.SubclassProc _callback;
    private Action? _invoked;
    public WindowsQuickEntryHotkeyBackend() => _callback = CallbackDepth.WrapSubclassProc(WindowProc);
    public bool Attach(IntPtr window, Action invoked)
    { _invoked = invoked; return Win32Helper.SetWindowSubclass(window, _callback, SubclassId, UIntPtr.Zero); }
    public bool Register(IntPtr window, GlobalHotkeyGesture gesture) => Win32Helper.RegisterHotKey(
        window, HotkeyId, (uint)gesture.Modifiers | 0x4000, (uint)gesture.VirtualKey);
    public void Unregister(IntPtr window) => Win32Helper.UnregisterHotKey(window, HotkeyId);
    public void Detach(IntPtr window)
    { Win32Helper.RemoveWindowSubclass(window, _callback, SubclassId); _invoked = null; }
    private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == GlobalHotkeyService.WmHotkey && wParam.ToUInt32() == HotkeyId)
        {
            DeskBox.Platform.UiDispatch.PostAfterCallback(() => _invoked?.Invoke());
            return IntPtr.Zero;
        }
        return Win32Helper.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
