using DeskBox.Models;

namespace DeskBox.ViewModels;

public sealed partial class TodoWidgetViewModel
{
    /// <summary>Adds one captured draft to this collection only after its snapshot commits.</summary>
    public Task<TodoItemViewModel?> AddCapturedItemAsync(string operationId, string? text) =>
        ExecuteAgendaMutationAsync(async () =>
        {
            if (_isDisposed || !IsInitialized || string.IsNullOrWhiteSpace(operationId)) return null;
            string normalized = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            TodoItemViewModel? existing = Items.FirstOrDefault(item => item.Id == operationId);
            if (existing is not null) return existing.Text == normalized ? existing : null;

            await _todoPersistenceGate.WaitAsync();
            try
            {
                var now = DateTimeOffset.UtcNow;
                var record = new TodoItem
                {
                    Id = operationId, Text = normalized, CreatedAt = now, UpdatedAt = now
                };
                List<TodoItem> snapshot = Items.Select(item => CloneTodoItem(item.Item)).ToList();
                bool insertAtBottom = NewTaskPosition == Services.SettingsService.TodoNewTaskPositionBottom;
                if (insertAtBottom) snapshot.Add(record); else snapshot.Insert(0, record);
                for (int index = 0; index < snapshot.Count; index++) snapshot[index].SortOrder = index;
                var liveIds = snapshot.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                snapshot.AddRange(_tombstones.Where(item => !liveIds.Contains(item.Id))
                    .OrderByDescending(item => item.UpdatedAt).Take(MaxRetainedTombstones).Select(CloneTodoItem));
                await _store.SaveAsync(new TodoWidgetData { Items = snapshot });

                var added = new TodoItemViewModel(record, _localizationService);
                // Collection observers run after insertion; their exceptions must
                // not turn a committed capture into an apparent failed submit.
                try { if (insertAtBottom) Items.Add(added); else Items.Insert(0, added); }
                catch (Exception ex) { App.Log($"[Todo] Capture saved; collection observer failed: {ex.Message}"); }
                try { NormalizeSortOrders(); RefreshVisibleItems(); RefreshCountProperties(); }
                catch (Exception ex) { App.Log($"[Todo] Capture saved; refresh failed: {ex.Message}"); }
                return added;
            }
            finally { _todoPersistenceGate.Release(); }
        });
}
