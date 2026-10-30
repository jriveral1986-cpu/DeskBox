using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using DeskBox.Views;

namespace DeskBox;

public partial class App
{
    private QuickEntryHotkeyService? _quickEntryHotkeyService;
    private QuickEntrySession? _quickEntryDraft;
    private QuickEntryWindow? _quickEntryWindow;
    private Task<QuickEntrySaveResult>? _quickEntryPendingSave;
    private bool _quickEntryStopping;

    private void InitializeQuickEntry(IntPtr trayWindow)
    {
        if (IsSettingsHost || _quickEntryStopping || _quickEntryHotkeyService is not null) return;
        _quickEntryDraft = new QuickEntrySession
        {
            Kind = SettingsService.Settings.QuickCapture.QuickEntryLastKind == "Task" ? "Task" : "Note",
            TodoWidgetId = SettingsService.Settings.QuickCapture.QuickEntryLastTodoWidgetId
        };
        _quickEntryHotkeyService = new QuickEntryHotkeyService(SettingsService, LocalizationService, ShowQuickEntryAsync);
        _quickEntryHotkeyService.Attach(trayWindow);
        SettingsService.SettingsChanged += OnQuickEntrySettingsChanged;
    }

    private Task ShowQuickEntryAsync()
    {
        if (_quickEntryStopping || _quickEntryDraft is null || WidgetManager is null) return Task.CompletedTask;
        _quickEntryWindow ??= new QuickEntryWindow(_quickEntryDraft, LocalizationService, ThemeService,
            () => WidgetManager.GetQuickEntryDestinations(), SaveQuickEntryAsync, async () =>
            {
                if (_quickEntryStopping) return null;
                ContentWidgetWindow window = await WidgetManager.CreateTodoWidgetAsync();
                await window.ContentReadyTask;
                return window.Identity.WidgetId;
            });
        _quickEntryWindow.ShowEntry();
        return Task.CompletedTask;
    }

    private Task<QuickEntrySaveResult> SaveQuickEntryAsync(QuickEntryRequest request)
    {
        if (_quickEntryStopping) return Task.FromResult(new QuickEntrySaveResult(false, "QuickEntry.SaveFailed"));
        return _quickEntryPendingSave = SaveQuickEntryCoreAsync(request);
    }

    private async Task<QuickEntrySaveResult> SaveQuickEntryCoreAsync(QuickEntryRequest request)
    {
        var router = new QuickEntrySaveRouter(async note =>
        {
            if (QuickCaptureService is null) return false;
            // The entry is plain text; never read the clipboard or import URLs.
            QuickCaptureWriteResult result = await QuickCaptureService.AddDetailedItemWithResultAsync(
                null, note.Text, QuickCaptureAppearancePreset.Default, TextContentFormat.PlainText,
                operationId: note.OperationId);
            return result.Saved;
        }, task => WidgetManager?.AddQuickEntryTaskAsync(task.TodoWidgetId, task.OperationId, task.Text) ?? Task.FromResult(false),
        id => WidgetManager?.GetQuickEntryDestinations().Any(target => target.Id == id) == true);
        QuickEntrySaveResult saved = await router.SaveAsync(request);
        if (!saved.Saved) return saved;
        SettingsService.Settings.QuickCapture.QuickEntryLastKind = request.Kind;
        if (request.Kind == "Task") SettingsService.Settings.QuickCapture.QuickEntryLastTodoWidgetId = request.TodoWidgetId;
        // The content is already durable. A history preference failure must not
        // misreport the content save and prompt the user to duplicate it.
        try
        {
            if (!await SettingsService.SaveCheckedAsync()) Log("[QuickEntry] Content saved; destination history could not be saved.");
        }
        catch (Exception ex) { Log($"[QuickEntry] Content saved; history failed: {ex.Message}"); }
        return new(true);
    }

    private void OnQuickEntrySettingsChanged()
    {
        if (_quickEntryStopping) return;
        if (UiDispatcherQueue?.HasThreadAccess == true) RefreshQuickEntrySettings();
        else UiDispatcherQueue?.TryEnqueue(RefreshQuickEntrySettings);
    }

    private void RefreshQuickEntrySettings()
    {
        if (_quickEntryStopping) return;
        _quickEntryHotkeyService?.RefreshRegistration();
        if (!SettingsService.Settings.QuickCapture.QuickEntryHotkeyEnabled)
        { _quickEntryWindow?.Dispose(); _quickEntryWindow = null; }
    }

    private async Task StopQuickEntryAsync()
    {
        _quickEntryStopping = true;
        SettingsService.SettingsChanged -= OnQuickEntrySettingsChanged;
        _quickEntryHotkeyService?.Dispose(); _quickEntryHotkeyService = null;
        if (_quickEntryPendingSave is { } pending)
        {
            try { await pending; }
            catch (Exception ex) { Log($"[QuickEntry] Pending save failed on shutdown: {ex.Message}"); }
        }
        _quickEntryWindow?.Dispose(); _quickEntryWindow = null;
    }
}
