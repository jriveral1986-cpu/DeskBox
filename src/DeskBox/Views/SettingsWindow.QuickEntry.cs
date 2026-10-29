using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views;

public sealed partial class SettingsWindow
{
    private QuickEntryHotkeyRuntimeState? _quickEntryHotkeyState;
    private bool _refreshingQuickEntrySettings;
    private bool _applyingQuickEntrySettings;
    private int _quickEntryRefreshGeneration;
    private ToggleSwitch? QuickEntryEnabledToggle => FindCreatedSectionElement<ToggleSwitch>("InteractionWindowSettings", "QuickEntryEnabledToggle");
    private Button? QuickEntryShortcutButton => FindCreatedSectionElement<Button>("InteractionWindowSettings", "QuickEntryShortcutButton");
    private TextBlock? QuickEntryShortcutStatus => FindCreatedSectionElement<TextBlock>("InteractionWindowSettings", "QuickEntryShortcutStatus");

    private async void QuickEntrySettingsPanel_Loaded(object sender, RoutedEventArgs args) => await RefreshQuickEntrySettingsAsync();

    private async Task RefreshQuickEntrySettingsAsync()
    {
        if (_isClosed || QuickEntryEnabledToggle is null || _applyingQuickEntrySettings) return;
        int generation = ++_quickEntryRefreshGeneration;
        try
        {
            QuickEntryHotkeyRuntimeState? state = await _hotkeyActions.GetQuickEntryHotkeyStateAsync();
            if (_isClosed || generation != _quickEntryRefreshGeneration || _applyingQuickEntrySettings) return;
            ApplyQuickEntryControls(state);
        }
        catch (Exception ex)
        {
            App.Log($"[Settings] Quick entry state unavailable: {ex.Message}");
            if (!_isClosed && generation == _quickEntryRefreshGeneration) ApplyQuickEntryControls(null);
        }
    }

    private void ApplyQuickEntryControls(QuickEntryHotkeyRuntimeState? state)
    {
        if (QuickEntryEnabledToggle is not { } toggle || QuickEntryShortcutButton is not { } button ||
            QuickEntryShortcutStatus is not { } status) return;
        _quickEntryHotkeyState = state;
        _refreshingQuickEntrySettings = true;
        toggle.IsOn = state?.Enabled == true;
        toggle.IsEnabled = state is not null && !_applyingQuickEntrySettings;
        button.IsEnabled = state is not null && !_applyingQuickEntrySettings;
        button.Content = GlobalHotkeyService.FormatGesture(state?.Gesture ?? QuickEntryPolicy.DefaultGesture, _localizationService);
        status.Text = state?.Error ?? _localizationService.T(state is null ? "QuickEntry.ShortcutFailed" :
            state.Enabled ? "QuickEntry.ShortcutEnabled" : "QuickEntry.ShortcutDisabled");
        _refreshingQuickEntrySettings = false;
    }

    private async void QuickEntryEnabledToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_refreshingQuickEntrySettings || _applyingQuickEntrySettings || sender is not ToggleSwitch toggle) return;
        _applyingQuickEntrySettings = true;
        toggle.IsEnabled = false;
        try { _quickEntryHotkeyState = await _hotkeyActions.SetQuickEntryHotkeyEnabledAsync(toggle.IsOn); }
        catch (Exception ex) { App.Log($"[Settings] Quick entry toggle failed: {ex.Message}"); }
        finally { _applyingQuickEntrySettings = false; ApplyQuickEntryControls(_quickEntryHotkeyState); }
    }

    private async void QuickEntryShortcutButton_Click(object sender, RoutedEventArgs args)
    {
        if (_applyingQuickEntrySettings || SettingsRoot.XamlRoot is null || _quickEntryHotkeyState is not { } state) return;
        var dialog = new HotkeyRecorderDialog(SettingsRoot.XamlRoot, _localizationService, _hotkeyActions,
            HotkeyRecorderDialog.Scope.QuickEntry, GlobalHotkeyActivation.FromChord(state.Gesture),
            [new HotkeyRecorderDialog.Preset(_localizationService.T("QuickEntry.DefaultShortcut"),
                GlobalHotkeyActivation.FromChord(QuickEntryPolicy.DefaultGesture))],
            async activation =>
            {
                _applyingQuickEntrySettings = true;
                try
                {
                    _quickEntryHotkeyState = await _hotkeyActions.ApplyQuickEntryHotkeyAsync(activation.Gesture);
                    return _quickEntryHotkeyState?.Error ?? (_quickEntryHotkeyState is null ? _localizationService.T("QuickEntry.ShortcutFailed") : null);
                }
                catch (Exception ex)
                { App.Log($"[Settings] Quick entry shortcut failed: {ex.Message}"); return _localizationService.T("QuickEntry.ShortcutFailed"); }
                finally { _applyingQuickEntrySettings = false; ApplyQuickEntryControls(_quickEntryHotkeyState); }
            },
            gesture => QuickEntryPolicy.ConflictsWithExisting(_settingsService.Settings, gesture)
                ? _localizationService.T("QuickEntry.ShortcutConflict") : null);
        await dialog.ShowAsync();
        await RefreshQuickEntrySettingsAsync();
    }
}
