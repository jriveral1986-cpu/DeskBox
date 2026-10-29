using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>Validates a capture destination immediately before invoking its real owner.</summary>
public sealed class QuickEntrySaveRouter(
    Func<QuickEntryRequest, Task<bool>> saveNote,
    Func<QuickEntryRequest, Task<bool>> saveTask,
    Func<string, bool> taskAvailable)
{
    public async Task<QuickEntrySaveResult> SaveAsync(QuickEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.Text)) return new(false);
        if (request.Text.Length > QuickCaptureService.MaxItemBodyCharacters) return new(false, "QuickEntry.TooLong");
        if (request.Kind == "Task")
        {
            if (string.IsNullOrWhiteSpace(request.TodoWidgetId) || !taskAvailable(request.TodoWidgetId))
                return new(false, "QuickEntry.NoTodo");
            bool committed = await saveTask(request);
            // Availability is a lifetime condition, not a preflight check only:
            // removal/disablement may have happened during the owner's IO.
            return committed && taskAvailable(request.TodoWidgetId) ? new(true) : new(false, "QuickEntry.NoTodo");
        }
        if (request.Kind != "Note") return new(false, "QuickEntry.SaveFailed");
        return await saveNote(request) ? new(true) : new(false, "QuickEntry.SaveFailed");
    }
}
