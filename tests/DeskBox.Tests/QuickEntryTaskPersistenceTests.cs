using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class QuickEntryTaskPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", "QuickEntryTask", Guid.NewGuid().ToString("N"));
    private TodoWidgetStore Store() => new(_root, "tasks");

    private async Task<TodoWidgetViewModel> OwnerAsync()
    {
        await Store().SaveAsync(new TodoWidgetData { Items =
        [
            new TodoItem { Id = "existing", Text = "keep me", SortOrder = 0, DailyPriorityDate = DateOnly.FromDateTime(DateTime.Now) }
        ] });
        var settings = new SettingsService();
        var owner = new TodoWidgetViewModel(Store(), new LocalizationService(settings), new WidgetConfig { Id = "tasks", WidgetKind = WidgetKind.Todo });
        await owner.InitializeAsync();
        return owner;
    }

    [Fact]
    public async Task CapturedTask_FailedWriteLeavesLiveCollectionPriorityAndOrderUnchanged()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        TodoItemViewModel existing = Assert.Single(owner.Items);
        DateOnly? priority = existing.DailyPriorityDate;
        using (var file = new FileStream(Store().StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => owner.AddCapturedItemAsync("draft-id", "new task"));
        }
        Assert.Same(existing, Assert.Single(owner.Items));
        Assert.Equal(priority, existing.DailyPriorityDate);
        Assert.Equal(0, existing.SortOrder);
        Assert.Single((await Store().LoadAsync()).Items);
        TodoItemViewModel? added = await owner.AddCapturedItemAsync("draft-id", "new task");
        Assert.NotNull(added);
        Assert.Equal(2, owner.Items.Count);
        Assert.Single((await Store().LoadAsync()).Items.Where(item => item.Text == "new task"));
    }

    [Fact]
    public async Task CapturedTask_CommitPreservesExistingViewModelsAndDoesNotInheritDailyPriority()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        TodoItemViewModel existing = Assert.Single(owner.Items);
        TodoItemViewModel? added = await owner.AddCapturedItemAsync("draft-id", "  captured task  ");
        Assert.NotNull(added);
        Assert.Equal("captured task", added.Text);
        Assert.Null(added.DailyPriorityDate);
        Assert.Same(existing, owner.Items.Single(item => item.Id == "existing"));
        Assert.NotNull(existing.DailyPriorityDate);
        Assert.Equal(new[] { "draft-id", "existing" }, (await Store().LoadAsync()).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task CapturedTask_ReplayedOperationIsAcknowledgedWithoutDuplicate()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        TodoItemViewModel? first = await owner.AddCapturedItemAsync("same-operation", "same draft");
        TodoItemViewModel? second = await owner.AddCapturedItemAsync("same-operation", "same draft");
        Assert.Same(first, second);
        Assert.Equal(2, owner.Items.Count);
        Assert.Equal(2, (await Store().LoadAsync()).Items.Count);
    }

    [Fact]
    public async Task CapturedTask_ObserverFailureCannotTurnDurableCommitIntoFailedSubmit()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        owner.Items.CollectionChanged += (_, _) => throw new InvalidOperationException("one view failed to refresh");
        TodoItemViewModel? added = await owner.AddCapturedItemAsync("draft-id", "saved despite observer");
        Assert.NotNull(added);
        Assert.Equal(2, owner.Items.Count);
        Assert.Contains((await Store().LoadAsync()).Items, item => item.Id == "draft-id");
        Assert.Same(added, await owner.AddCapturedItemAsync("draft-id", "saved despite observer"));
    }

    [Fact]
    public async Task CapturedTask_ConcurrentRepeatedOperationSharesOwnerMutationLane()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        Task<TodoItemViewModel?> first = owner.AddCapturedItemAsync("once", "once");
        Task<TodoItemViewModel?> second = owner.AddCapturedItemAsync("once", "once");
        TodoItemViewModel?[] results = await Task.WhenAll(first, second);
        Assert.Same(results[0], results[1]);
        Assert.Equal(2, owner.Items.Count);
        Assert.Equal(2, (await Store().LoadAsync()).Items.Count);
    }

    [Fact]
    public async Task CapturedTask_OwnerDisposedDuringPendingCommit_DoesNotPublishOrAcknowledgeSuccess()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        TodoItemViewModel original = Assert.Single(owner.Items);
        Task<TodoItemViewModel?> pending;
        using (var blocked = new FileStream(Store().StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            pending = owner.AddCapturedItemAsync("late-capture", "retain this draft");
            Assert.False(pending.IsCompleted);
            owner.Dispose();
        }

        TodoItemViewModel? result = await pending;

        Assert.Null(result);
        Assert.Same(original, Assert.Single(owner.Items));
        Assert.DoesNotContain(owner.VisibleItems, item => item.Id == "late-capture");
    }

    [Fact]
    public async Task TaskPanelRemovedDuringRealAsyncCommit_RouterRetainsDraftInsteadOfAcknowledgingOrphanedSave()
    {
        using TodoWidgetViewModel owner = await OwnerAsync();
        var destination = new WidgetConfig { Id = "tasks", WidgetKind = WidgetKind.Todo };
        var availablePanels = new List<WidgetConfig> { destination };
        var session = new QuickEntrySession { Kind = "Task", TodoWidgetId = destination.Id, Text = "keep my task" };
        string operationId = session.OperationId;
        var router = new QuickEntrySaveRouter(
            _ => throw new InvalidOperationException("Task capture must never fall back to a note."),
            async request => await owner.AddCapturedItemAsync(request.OperationId, request.Text) is not null,
            id => availablePanels.Any(panel => panel.Id == id && !panel.IsDisabled));
        Task<QuickEntrySaveResult> pending;
        using (var blocked = new FileStream(Store().StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            pending = session.SaveAsync(router.SaveAsync);
            Assert.False(pending.IsCompleted);
            // This is the real registration-removal boundary, while the owner
            // is awaiting its actual file commit, rather than a writer stub
            // whose result was predetermined to be false.
            availablePanels.Clear();
            await TodoWidgetStore.DeleteForWidgetAsync(_root, destination.Id);
        }

        QuickEntrySaveResult result = await pending;

        Assert.False(result.Saved);
        Assert.Equal("QuickEntry.NoTodo", result.Error);
        Assert.Equal("keep my task", session.Text);
        Assert.Equal(destination.Id, session.TodoWidgetId);
        Assert.Equal(operationId, session.OperationId);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
