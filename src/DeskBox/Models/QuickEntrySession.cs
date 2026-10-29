namespace DeskBox.Models;

public sealed record QuickEntryRequest(string OperationId, string Text, string Kind, string TodoWidgetId);
public sealed record QuickEntrySaveResult(bool Saved, string? Error = null);

/// <summary>Session draft and one in-flight durable submission, independent of the window.</summary>
public sealed class QuickEntrySession
{
    private int _saving;
    private QuickEntryRequest? _lastAttempt;
    public string Text { get; set; } = string.Empty;
    public string Kind { get; set; } = "Note";
    public string TodoWidgetId { get; set; } = string.Empty;
    public string OperationId { get; private set; } = Guid.NewGuid().ToString("N");
    public bool IsSaving => Volatile.Read(ref _saving) != 0;

    public async Task<QuickEntrySaveResult> SaveAsync(Func<QuickEntryRequest, Task<QuickEntrySaveResult>> save)
    {
        if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0) return new(false);
        try
        {
            if (string.IsNullOrWhiteSpace(Text)) return new(false);
            if (_lastAttempt is { } attempted &&
                (attempted.Text != Text.Trim() || attempted.Kind != Kind || attempted.TodoWidgetId != TodoWidgetId))
                OperationId = Guid.NewGuid().ToString("N");
            var request = new QuickEntryRequest(OperationId, Text.Trim(), Kind, TodoWidgetId);
            _lastAttempt = request;
            QuickEntrySaveResult result = await save(request);
            if (result.Saved)
            {
                if (Text.Trim() == request.Text && Kind == request.Kind && TodoWidgetId == request.TodoWidgetId)
                    Text = string.Empty;
                OperationId = Guid.NewGuid().ToString("N");
                _lastAttempt = null;
            }
            return result;
        }
        finally { Volatile.Write(ref _saving, 0); }
    }
}
