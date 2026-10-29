using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.ViewModels;

public sealed partial class TodoWidgetViewModel
{
    private DateOnly _agendaDate = DateOnly.FromDateTime(DateTime.Now);
    private readonly SemaphoreSlim _agendaMutationGate = new(1, 1);
    private readonly SemaphoreSlim _todoPersistenceGate = new(1, 1);
    private PendingDailyPriorityChange? _pendingDailyPriorityChange;

    private sealed record PendingDailyPriorityChange(
        string ItemId,
        DateOnly? PreviousDate,
        DateTimeOffset PreviousUpdatedAt,
        DateTimeOffset ProposedUpdatedAt);

    public DateOnly AgendaDate => _agendaDate;

    public bool CanManuallyReorder => SelectedFilter != TodoFilter.Today;

    public int DailyPriorityCount => Items.Count(item =>
        TodoTodayAgendaPolicy.IsDailyPriority(item.Item, _agendaDate));

    public bool IsDailyPriority(TodoItemViewModel item) =>
        TodoTodayAgendaPolicy.IsDailyPriority(item.Item, _agendaDate);

    public bool CanSelectDailyPriority(TodoItemViewModel item) =>
        !item.IsCompleted && (IsDailyPriority(item) ||
            DailyPriorityCount < TodoTodayAgendaPolicy.MaximumDailyPriorities);

    public void RefreshAgendaDate(DateOnly? localDate = null)
    {
        DateOnly date = localDate ?? DateOnly.FromDateTime(DateTime.Now);
        if (_agendaDate == date) return;
        _agendaDate = date;
        OnPropertyChanged(nameof(AgendaDate));
        foreach (TodoItemViewModel item in Items) item.RefreshLocalizedText();
        RefreshVisibleItems();
        RefreshCountProperties();
    }

    // Slot changes, reopening and collection/snapshot replacement share a lane.
    // Callers must invoke a core mutation here, never another gated entry point.
    private async Task<T> ExecuteAgendaMutationAsync<T>(Func<Task<T>> operation)
    {
        await _agendaMutationGate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _agendaMutationGate.Release();
        }
    }

    public Task<bool> SetDailyPriorityAsync(string itemId, bool selected) =>
        ExecuteAgendaMutationAsync(() => SetDailyPriorityCoreAsync(itemId, selected));

    private async Task<bool> SetDailyPriorityCoreAsync(string itemId, bool selected)
    {
        TodoItemViewModel? item = FindItem(itemId);
        if (_isDisposed || item is null || (selected && !CanSelectDailyPriority(item))) return false;
        DateOnly? date = selected ? _agendaDate : null;
        if (item.DailyPriorityDate == date) return true;
        DateOnly? previous = item.DailyPriorityDate;
        DateTimeOffset previousUpdated = item.UpdatedAt;
        var pending = new PendingDailyPriorityChange(item.Id, previous, previousUpdated, DateTimeOffset.UtcNow);
        _pendingDailyPriorityChange = pending;
        item.UpdatedAt = pending.ProposedUpdatedAt;
        item.DailyPriorityDate = date;
        try
        {
            await SaveSnapshotAsync(persistPriorityChange: true);
        }
        catch (Exception ex)
        {
            item.DailyPriorityDate = previous;
            if (item.UpdatedAt == pending.ProposedUpdatedAt)
                item.UpdatedAt = previousUpdated;
            _pendingDailyPriorityChange = null;
            RefreshVisibleItems();
            RefreshCountProperties();
            App.Log($"[Todo] Daily priority could not be saved: {ex.Message}");
            return false;
        }
        RefreshVisibleItems();
        RefreshCountProperties();
        return true;
    }

    private bool IsInTodayAgenda(TodoItemViewModel item) =>
        TodoTodayAgendaPolicy.GetSection(item.Item, _agendaDate) is not null;

    private List<TodoItemViewModel> BuildAgendaItems(List<TodoItemViewModel> items)
    {
        items.Sort((left, right) => TodoTodayAgendaPolicy.Compare(left.Item, right.Item, _agendaDate));
        TodoAgendaSection? lastSection = null;
        foreach (TodoItemViewModel item in items)
        {
            TodoAgendaSection? section = TodoTodayAgendaPolicy.GetSection(item.Item, _agendaDate);
            if (section == lastSection) continue;
            string key = section switch
            {
                TodoAgendaSection.Priorities => "Todo.Agenda.Priorities",
                TodoAgendaSection.Overdue => "Todo.Agenda.Overdue",
                TodoAgendaSection.Today => "Todo.Agenda.Today",
                _ => "Todo.Agenda.Upcoming"
            };
            item.SetAgendaSectionHeader(_localizationService.T(key));
            lastSection = section;
        }
        return items;
    }
}
