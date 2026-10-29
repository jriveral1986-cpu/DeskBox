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
