using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class TodoTodayAgendaTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTime _today = DateTime.Today;

    [Fact]
    public async Task Today_IncludesOverdueTasksBeforeTasksDueToday()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            TaskItem("today", 0, 0),
            TaskItem("overdue", 1, -1));

        viewModel.SelectedFilter = TodoFilter.Today;

        Assert.Equal(new[] { "overdue", "today" }, viewModel.VisibleItems.Select(item => item.Id));
        Assert.Equal(2, viewModel.TodayFilterCount);
    }

    [Fact]
    public async Task Today_IncludesTheNextSevenLocalDatesButNotTheEighth()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            TaskItem("eighth-day", 0, 8),
            TaskItem("seventh-day", 1, 7),
            TaskItem("tomorrow", 2, 1),
            TaskItem("today", 3, 0),
            new TodoItem { Id = "undated", Text = "undated", SortOrder = 4 });

        viewModel.SelectedFilter = TodoFilter.Today;

        Assert.Equal(
            new[] { "today", "tomorrow", "seventh-day" },
            viewModel.VisibleItems.Select(item => item.Id));
        Assert.Equal(3, viewModel.TodayFilterCount);
    }

    [Fact]
    public async Task Today_OrdersByDueTimeWithoutRewritingManualOrder()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            TaskItem("later", 0, 0, 18),
            TaskItem("earlier", 1, 0, 9));

        viewModel.SelectedFilter = TodoFilter.Today;

        Assert.Equal(new[] { "earlier", "later" }, viewModel.VisibleItems.Select(item => item.Id));
        Assert.Equal(new[] { "later", "earlier" }, viewModel.Items.Select(item => item.Id));
        Assert.Equal(new[] { 0, 1 }, viewModel.Items.Select(item => item.SortOrder));

        viewModel.SelectedFilter = TodoFilter.All;
        Assert.Equal(new[] { "later", "earlier" }, viewModel.VisibleItems.Select(item => item.Id));
    }

    [Fact]
    public async Task Today_RejectsManualReorderWithoutChangingPersistedOrder()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            TaskItem("first", 0, 0),
            TaskItem("second", 1, 0));
        viewModel.SelectedFilter = TodoFilter.Today;

        bool moved = await viewModel.MoveItemAsync("second", 0);

        Assert.False(moved);
        Assert.Equal(new[] { "first", "second" }, viewModel.Items.Select(item => item.Id));
        TodoWidgetData persisted = await CreateStore().LoadAsync();
        Assert.Equal(new[] { "first", "second" }, persisted.Items.Select(item => item.Id));
    }

    [Fact]
    public void DailyPriorityDate_SurvivesGeneratedJsonRoundTripWithoutChangingDueDateOrImportance()
    {
        const string json = """
            {"id":"priority","text":"priority","isImportant":false,"dailyPriorityDate":"2026-10-29"}
            """;
        TodoItem item = JsonSerializer.Deserialize(json, TodoJsonContext.Default.TodoItem)!;

        string saved = JsonSerializer.Serialize(item, TodoJsonContext.Default.TodoItem);
        using JsonDocument document = JsonDocument.Parse(saved);

        Assert.True(document.RootElement.TryGetProperty("dailyPriorityDate", out JsonElement date));
        Assert.Equal("2026-10-29", date.GetString());
        Assert.False(item.IsImportant);
        Assert.Null(item.DueDate);
    }

    [Fact]
    public async Task PrioritySelection_EnforcesThreeSlotsAndPersistsWithoutChangingTaskMetadata()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            new TodoItem { Id = "one", Text = "one", SortOrder = 0 },
            new TodoItem { Id = "two", Text = "two", SortOrder = 1 },
            new TodoItem { Id = "three", Text = "three", SortOrder = 2 },
            new TodoItem { Id = "four", Text = "four", SortOrder = 3 });
        Task<bool>[] selections = viewModel.Items
            .Select(item => viewModel.SetDailyPriorityAsync(item.Id, true)).ToArray();

        bool[] results = await Task.WhenAll(selections);

        Assert.Equal(3, results.Count(result => result));
        Assert.Equal(3, viewModel.DailyPriorityCount);
        Assert.All(viewModel.Items, item =>
        {
            Assert.Null(item.DueDate);
            Assert.False(item.IsImportant);
        });
        TodoWidgetData saved = await CreateStore().LoadAsync();
        Assert.Equal(3, saved.Items.Count(item => item.DailyPriorityDate == viewModel.AgendaDate));
        viewModel.SelectedFilter = TodoFilter.Today;
        Assert.Equal(3, viewModel.VisibleItems.Count);
        Assert.All(viewModel.VisibleItems, item => Assert.True(viewModel.IsDailyPriority(item)));
        Assert.Single(viewModel.VisibleItems.Where(item => item.AgendaSectionVisibility == Microsoft.UI.Xaml.Visibility.Visible));
    }

    [Fact]
    public async Task Completion_FreesPrioritySlotAndReopeningDoesNotExceedTheLimit()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            new TodoItem { Id = "one", Text = "one", SortOrder = 0 },
            new TodoItem { Id = "two", Text = "two", SortOrder = 1 },
            new TodoItem { Id = "three", Text = "three", SortOrder = 2 },
            new TodoItem { Id = "four", Text = "four", SortOrder = 3 });
        foreach (string id in new[] { "one", "two", "three" })
            Assert.True(await viewModel.SetDailyPriorityAsync(id, true));

        Assert.True(await viewModel.SetCompletedAsync("one", true));
        Assert.Equal(2, viewModel.DailyPriorityCount);
        Assert.True(await viewModel.SetDailyPriorityAsync("four", true));
        Assert.True(await viewModel.SetCompletedAsync("one", false));

        Assert.Equal(3, viewModel.DailyPriorityCount);
        Assert.Null(viewModel.Items.Single(item => item.Id == "one").DailyPriorityDate);
    }

    [Fact]
    public async Task UnselectingPriority_FreesSlotWithoutRemovingTask()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            new TodoItem { Id = "one", Text = "one" });
        Assert.True(await viewModel.SetDailyPriorityAsync("one", true));

        Assert.True(await viewModel.SetDailyPriorityAsync("one", false));

        Assert.Equal(0, viewModel.DailyPriorityCount);
        Assert.Null(Assert.Single(viewModel.Items).DailyPriorityDate);
        Assert.Single((await CreateStore().LoadAsync()).Items);
    }

    [Fact]
    public async Task PriorityAppearsOnceAndHeadersResetOutsideToday()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            TaskItem("principal", 0, -2), TaskItem("overdue", 1, -1),
            TaskItem("today", 2, 0), TaskItem("future", 3, 1));
        Assert.True(await viewModel.SetDailyPriorityAsync("principal", true));

        viewModel.SelectedFilter = TodoFilter.Today;

        Assert.Equal(new[] { "principal", "overdue", "today", "future" },
            viewModel.VisibleItems.Select(item => item.Id));
        Assert.Equal(4, viewModel.VisibleItems.Select(item => item.AgendaSectionText).Distinct().Count());
        Assert.Equal(4, viewModel.VisibleItems.Select(item => item.Id).Distinct().Count());
        viewModel.SelectedFilter = TodoFilter.All;
        Assert.All(viewModel.VisibleItems, item => Assert.Equal(string.Empty, item.AgendaSectionText));
    }

    [Fact]
    public async Task DateRefresh_ExpiresPriorityWithoutChangingTaskOrEditorState()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            new TodoItem { Id = "undated", Text = "keep this text" });
        Assert.True(await viewModel.SetDailyPriorityAsync("undated", true));
        TodoItemViewModel original = Assert.Single(viewModel.Items);
        DateOnly previousDate = viewModel.AgendaDate;
        DateTimeOffset previousUpdated = original.UpdatedAt;
        viewModel.InputText = "unsaved draft";
        viewModel.SelectedFilter = TodoFilter.Today;

        viewModel.RefreshAgendaDate(previousDate.AddDays(1));

        Assert.Equal(0, viewModel.DailyPriorityCount);
        Assert.Empty(viewModel.VisibleItems);
        Assert.Same(original, Assert.Single(viewModel.Items));
        Assert.Equal(previousDate, original.DailyPriorityDate);
        Assert.Equal(previousUpdated, original.UpdatedAt);
        Assert.Equal("keep this text", original.Text);
        Assert.Equal("unsaved draft", viewModel.InputText);
    }

    [Fact]
    public async Task Today_RespectsColorFilterAndOrdersCompletedWithinItsTemporalSection()
    {
        TodoItem overdueDone = TaskItem("overdue-done", 0, -1);
        overdueDone.IsCompleted = true;
        overdueDone.ColorMarker = TodoItem.RedColorMarker;
        TodoItem overduePending = TaskItem("overdue-pending", 1, -1);
        overduePending.ColorMarker = TodoItem.RedColorMarker;
        TodoItem today = TaskItem("today", 2, 0);
        today.ColorMarker = TodoItem.RedColorMarker;
        using TodoWidgetViewModel viewModel = await CreateAsync(
            overdueDone, overduePending, today, TaskItem("other-color", 3, 0));
        viewModel.SelectedColorFilter = TodoColorFilter.Red;
        viewModel.SelectedFilter = TodoFilter.Today;
        viewModel.ShowCompletedTasks = true;

        Assert.Equal(new[] { "overdue-pending", "overdue-done", "today" },
            viewModel.VisibleItems.Select(item => item.Id));

        viewModel.ShowCompletedTasks = false;
        Assert.Equal(new[] { "overdue-pending", "today" }, viewModel.VisibleItems.Select(item => item.Id));
    }

    [Theory]
    [InlineData(2026, 12, 31)]
    [InlineData(2028, 2, 28)]
    public void AgendaPolicy_UsesCalendarDayBoundariesAcrossMonthAndYear(int year, int month, int day)
    {
        DateOnly date = new(year, month, day);
        DateTime seventh = date.AddDays(7).ToDateTime(new TimeOnly(23, 59));
        DateTime eighth = date.AddDays(8).ToDateTime(TimeOnly.MinValue);
        TodoItem seventhItem = new()
        {
            DueDate = new DateTimeOffset(seventh, TimeZoneInfo.Local.GetUtcOffset(seventh))
        };
        TodoItem eighthItem = new()
        {
            DueDate = new DateTimeOffset(eighth, TimeZoneInfo.Local.GetUtcOffset(eighth))
        };

        Assert.Equal(TodoAgendaSection.Upcoming, TodoTodayAgendaPolicy.GetSection(seventhItem, date));
        Assert.Null(TodoTodayAgendaPolicy.GetSection(eighthItem, date));
    }

    [Fact]
    public void AgendaPolicy_ConvertsDueInstantToLocalCalendarDate()
    {
        DateTimeOffset instant = new(2026, 10, 29, 0, 30, 0, TimeSpan.FromHours(14));
        DateOnly localDay = DateOnly.FromDateTime(instant.ToLocalTime().DateTime);

        Assert.Equal(TodoAgendaSection.Today,
            TodoTodayAgendaPolicy.GetSection(new TodoItem { DueDate = instant }, localDay));
        Assert.Equal(TodoAgendaSection.Overdue,
            TodoTodayAgendaPolicy.GetSection(new TodoItem { DueDate = instant }, localDay.AddDays(1)));
    }

    [Fact]
    public async Task PrioritySaveFailure_RollsBackSelectionAndPreservesDiskAndTask()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(
            new TodoItem { Id = "one", Text = "do not lose this" });
        TodoItemViewModel item = Assert.Single(viewModel.Items);
        DateTimeOffset updated = item.UpdatedAt;
        bool selected;
        using (var lockedFile = new FileStream(CreateStore().StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            selected = await viewModel.SetDailyPriorityAsync(item.Id, true);
        }

        Assert.False(selected);
        Assert.Null(item.DailyPriorityDate);
        Assert.Equal(updated, item.UpdatedAt);
        Assert.Equal("do not lose this", item.Text);
        TodoItem persisted = Assert.Single((await CreateStore().LoadAsync()).Items);
        Assert.Null(persisted.DailyPriorityDate);
        Assert.Equal(item.Text, persisted.Text);
    }

    [Fact]
    public void Recurrence_DoesNotInheritDailyPriority()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var item = new TodoItem
        {
            Id = "recurring", Text = "daily", DueDate = now,
            DailyPriorityDate = DateOnly.FromDateTime(now.DateTime),
            Recurrence = new TodoRecurrence { Mode = TodoRecurrenceMode.Daily }
        };

        Assert.True(TodoRecurrenceService.TryCreateNextOccurrence(item, now, out TodoItem? next));
        Assert.NotNull(next);
        Assert.Null(next.DailyPriorityDate);
    }

    [Fact]
    public async Task UndoDelete_PreservesPriorityDate()
    {
        using TodoWidgetViewModel viewModel = await CreateAsync(new TodoItem { Id = "one", Text = "one" });
        Assert.True(await viewModel.SetDailyPriorityAsync("one", true));
        DateOnly selectedDate = viewModel.AgendaDate;

        await viewModel.DeleteItemAsync("one");
        Assert.True(await viewModel.UndoLastActionAsync());

        Assert.Equal(selectedDate, Assert.Single(viewModel.Items).DailyPriorityDate);
        Assert.Equal(1, viewModel.DailyPriorityCount);
    }

    private TodoItem TaskItem(string id, int order, int days, int hour = 12)
    {
        DateTime date = _today.AddDays(days).AddHours(hour);
        return new TodoItem
        {
            Id = id,
            Text = id,
            SortOrder = order,
            DueDate = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date))
        };
    }

    private TodoWidgetStore CreateStore() => new(_root, "agenda");

    private async Task<TodoWidgetViewModel> CreateAsync(params TodoItem[] items)
    {
        await CreateStore().SaveAsync(new TodoWidgetData { Items = items.ToList() });
        var viewModel = new TodoWidgetViewModel(
            CreateStore(),
            TestServices.CreateLocalizationService(),
            new WidgetConfig { Id = "agenda", Name = "Agenda", WidgetKind = WidgetKind.Todo });
        await viewModel.InitializeAsync();
        return viewModel;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
