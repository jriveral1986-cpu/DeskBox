using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryBehaviorTests
{
    [Theory]
    [InlineData(3, 0x4E, true)]
    [InlineData(6, 0x51, true)]
    [InlineData(8, 0x20, false)]
    [InlineData(1, 0x20, false)]
    [InlineData(1, 0x73, false)]
    [InlineData(1, 0x09, false)]
    [InlineData(3, 0x2E, false)]
    [InlineData(6, 0x1B, false)]
    [InlineData(0, 0x4E, false)]
    [InlineData(16, 0x4E, false)]
    public void ShortcutPolicy_RejectsSystemReservedAndIncompleteChords(int modifiers, int key, bool allowed) =>
        Assert.Equal(allowed, QuickEntryPolicy.IsAllowedGesture(new((HotkeyModifierKeys)modifiers, key)));

    [Fact]
    public void ShortcutPolicy_RecognizesBothEnabledOwnersAndCapture()
    {
        var settings = new AppSettings();
        settings.Core.GlobalHotkeyModifiers = 3;
        settings.Core.GlobalHotkeyKey = 0x4E;
        Assert.True(QuickEntryPolicy.ConflictsWithExisting(settings, QuickEntryPolicy.DefaultGesture));
        settings.Core.GlobalHotkeyEnabled = false;
        settings.Search.SearchHotkeyEnabled = true;
        settings.Search.SearchHotkeyModifiers = 3;
        settings.Search.SearchHotkeyKey = 0x4E;
        Assert.True(QuickEntryPolicy.ConflictsWithExisting(settings, QuickEntryPolicy.DefaultGesture));
        settings.Search.SearchHotkeyEnabled = false;
        Assert.False(QuickEntryPolicy.ConflictsWithExisting(settings, QuickEntryPolicy.DefaultGesture));
        Assert.False(QuickEntryPolicy.IsOwnedByCapture(settings, QuickEntryPolicy.DefaultGesture));
        settings.QuickCapture.QuickEntryHotkeyEnabled = true;
        Assert.True(QuickEntryPolicy.IsOwnedByCapture(settings, QuickEntryPolicy.DefaultGesture));
    }

    [Fact]
    public async Task FailedSave_PreservesDraftDestinationAndOperationIdForRetry()
    {
        var session = new QuickEntrySession { Text = "my draft", Kind = "Task", TodoWidgetId = "todo-1" };
        string id = session.OperationId;
        QuickEntrySaveResult failed = await session.SaveAsync(_ => Task.FromResult(new QuickEntrySaveResult(false, "disk")));
        Assert.False(failed.Saved);
        Assert.Equal("my draft", session.Text);
        Assert.Equal("Task", session.Kind);
        Assert.Equal("todo-1", session.TodoWidgetId);
        Assert.Equal(id, session.OperationId);
        Assert.False(session.IsSaving);
        await session.SaveAsync(request =>
        {
            Assert.Equal(id, request.OperationId);
            return Task.FromResult(new QuickEntrySaveResult(true));
        });
        Assert.Empty(session.Text);
        Assert.NotEqual(id, session.OperationId);
        Assert.Equal("todo-1", session.TodoWidgetId);
    }

    [Fact]
    public async Task ThrowingSave_LeavesDraftEditableAndRetainsOperationId()
    {
        var session = new QuickEntrySession { Text = "draft" };
        string id = session.OperationId;
        await Assert.ThrowsAsync<IOException>(() => session.SaveAsync(_ => throw new IOException("disk unavailable")));
        Assert.False(session.IsSaving);
        Assert.Equal("draft", session.Text);
        Assert.Equal(id, session.OperationId);
    }

    [Fact]
    public async Task ConcurrentSubmit_OnlyInvokesOneSave()
    {
        var session = new QuickEntrySession { Text = "once" };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<QuickEntrySaveResult> first = session.SaveAsync(async _ =>
        {
            calls++; started.SetResult(); await release.Task; return new(true);
        });
        await started.Task;
        QuickEntrySaveResult second = await session.SaveAsync(_ =>
        { calls++; return Task.FromResult(new QuickEntrySaveResult(true)); });
        Assert.False(second.Saved);
        release.SetResult();
        Assert.True((await first).Saved);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WhitespaceDraft_DoesNotInvokeWriter()
    {
        var session = new QuickEntrySession { Text = " \n\t " };
        Assert.False((await session.SaveAsync(_ => throw new InvalidOperationException("must not write"))).Saved);
        Assert.False(session.IsSaving);
    }

    [Fact]
    public async Task DraftEditedDuringSave_IsNotClearedByPreviousRequest()
    {
        var session = new QuickEntrySession { Text = "first" };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<QuickEntrySaveResult> pending = session.SaveAsync(async _ => { await release.Task; return new(true); });
        session.Text = "new draft";
        release.SetResult();
        Assert.True((await pending).Saved);
        Assert.Equal("new draft", session.Text);
    }

    [Fact]
    public async Task ChangedFailedPayload_GetsNewOperationIdRatherThanAcknowledgingOldContent()
    {
        var session = new QuickEntrySession { Text = "first" };
        string previous = session.OperationId;
        await session.SaveAsync(_ => Task.FromResult(new QuickEntrySaveResult(false)));
        session.Text = "edited";
        await session.SaveAsync(request =>
        {
            Assert.NotEqual(previous, request.OperationId);
            Assert.Equal("edited", request.Text);
            return Task.FromResult(new QuickEntrySaveResult(true));
        });
    }

    [Fact]
    public void MigrationFrom15_LeavesCaptureOptInAndKeepsExplicitDestinations()
    {
        var source = new AppSettings { SchemaVersion = 15 };
        source.QuickCapture.QuickEntryLastKind = "Task";
        source.QuickCapture.QuickEntryLastTodoWidgetId = "stable-id";
        (AppSettings migrated, bool applied) = new SettingsMigrationPipeline().RunMigrationsOnCopy(source);
        Assert.True(applied);
        Assert.Equal(16, migrated.SchemaVersion);
        Assert.False(migrated.QuickCapture.QuickEntryHotkeyEnabled);
        Assert.Equal("Task", migrated.QuickCapture.QuickEntryLastKind);
        Assert.Equal("stable-id", migrated.QuickCapture.QuickEntryLastTodoWidgetId);
        Assert.Equal(3, migrated.QuickCapture.QuickEntryHotkeyModifiers);
    }
}
