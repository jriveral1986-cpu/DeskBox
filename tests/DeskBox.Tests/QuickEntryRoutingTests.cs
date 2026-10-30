using System.Text.Json;
using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryRoutingTests
{
    [Fact]
    public async Task NoteDestination_InvokesOnlyNoteOwnerWithStableOperationId()
    {
        QuickEntryRequest? delivered = null;
        var router = new QuickEntrySaveRouter(note => { delivered = note; return Task.FromResult(true); },
            _ => throw new InvalidOperationException("wrong owner"), _ => false);
        QuickEntrySaveResult result = await router.SaveAsync(new("once", "note", "Note", "old-task-id"));
        Assert.True(result.Saved);
        Assert.Equal("once", delivered!.OperationId);
        Assert.Equal("note", delivered.Text);
    }

    [Fact]
    public async Task TaskDestination_InvokesOnlyChosenOwnerAndNeverSelectsAnotherPanel()
    {
        string? destination = null;
        var router = new QuickEntrySaveRouter(_ => throw new InvalidOperationException("wrong owner"),
            task => { destination = task.TodoWidgetId; return Task.FromResult(true); }, id => id == "chosen");
        Assert.True((await router.SaveAsync(new("once", "task", "Task", "chosen"))).Saved);
        Assert.Equal("chosen", destination);
    }

    [Theory]
    [InlineData("")]
    [InlineData("deleted")]
    [InlineData("disabled")]
    public async Task MissingDestination_PreservesDraftWithoutCreatingPanelOrWritingNote(string target)
    {
        var session = new QuickEntrySession { Text = "do not lose", Kind = "Task", TodoWidgetId = target };
        var router = new QuickEntrySaveRouter(_ => throw new InvalidOperationException("must not save note"),
            _ => throw new InvalidOperationException("must not save to fallback"), _ => false);
        QuickEntrySaveResult result = await session.SaveAsync(router.SaveAsync);
        Assert.False(result.Saved);
        Assert.Equal("QuickEntry.NoTodo", result.Error);
        Assert.Equal("do not lose", session.Text);
        Assert.Equal(target, session.TodoWidgetId);
    }

    [Fact]
    public async Task DestinationDisappearsDuringSave_ReportsFailureAndKeepsDraft()
    {
        var session = new QuickEntrySession { Text = "keep", Kind = "Task", TodoWidgetId = "chosen" };
        var router = new QuickEntrySaveRouter(_ => throw new InvalidOperationException("wrong owner"),
            _ => Task.FromResult(false), _ => true);
        Assert.False((await session.SaveAsync(router.SaveAsync)).Saved);
        Assert.Equal("keep", session.Text);
    }

    [Fact]
    public async Task OversizedNote_IsNotSilentlyTruncatedOrCleared()
    {
        var session = new QuickEntrySession { Text = new string('x', QuickCaptureService.MaxItemBodyCharacters + 1) };
        var router = new QuickEntrySaveRouter(_ => throw new InvalidOperationException("must not write"),
            _ => throw new InvalidOperationException("must not write"), _ => true);
        QuickEntrySaveResult result = await session.SaveAsync(router.SaveAsync);
        Assert.False(result.Saved);
        Assert.Equal("QuickEntry.TooLong", result.Error);
        Assert.Equal(QuickCaptureService.MaxItemBodyCharacters + 1, session.Text.Length);
    }

    [Fact]
    public void RuntimeShortcutState_UsesSourceGeneratedIpcSerialization()
    {
        var state = new QuickEntryHotkeyRuntimeState(true, true, QuickEntryPolicy.DefaultGesture, null);
        string json = JsonSerializer.Serialize(state, SettingsIpcJsonContext.Default.QuickEntryHotkeyRuntimeState);
        QuickEntryHotkeyRuntimeState? restored = JsonSerializer.Deserialize(json, SettingsIpcJsonContext.Default.QuickEntryHotkeyRuntimeState);
        Assert.Equal(state, restored);
    }
}
