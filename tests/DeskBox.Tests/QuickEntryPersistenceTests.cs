using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DeskBox.Tests", "QuickEntryPersistence", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FailedNoteWrite_DoesNotPublishNewNoteOrReorderExistingNotes()
    {
        QuickCaptureStore store = CreateStore();
        var service = new QuickCaptureService(store);
        await service.AddDetailedItemWithResultAsync(
            null, "first existing note", QuickCaptureAppearancePreset.Default, pin: true);
        await service.AddDetailedItemWithResultAsync(
            null, "second existing note", QuickCaptureAppearancePreset.Default, pin: true);
        QuickCaptureStoreData before = await service.GetDataAsync();
        int changedCount = 0;
        service.Changed += () => changedCount++;

        using (var locked = new FileStream(store.StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => service.AddDetailedItemWithResultAsync(
                null, "must remain a draft", QuickCaptureAppearancePreset.Default, pin: true));
        }

        QuickCaptureStoreData after = await service.GetDataAsync();
        Assert.Equal(0, changedCount);
        Assert.Equal(before.Items.Select(item => item.Id), after.Items.Select(item => item.Id));
        Assert.Equal(before.Items.Select(item => item.SortOrder), after.Items.Select(item => item.SortOrder));
        Assert.Equal(before.Items.Select(item => item.PinnedSortOrder), after.Items.Select(item => item.PinnedSortOrder));
        Assert.DoesNotContain(after.Items, item => item.Body == "must remain a draft");
    }

    [Fact]
    public async Task FailedNoteWrite_RetryPersistsExactlyOneNewNote()
    {
        QuickCaptureStore store = CreateStore();
        var service = new QuickCaptureService(store);
        await service.AddDetailedItemWithResultAsync(
            null, "existing note", QuickCaptureAppearancePreset.Default);
        await service.GetDataAsync();

        using (var locked = new FileStream(store.StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => service.AddDetailedItemWithResultAsync(
                null, "retry this draft", QuickCaptureAppearancePreset.Default));
        }

        QuickCaptureWriteResult saved = await service.AddDetailedItemWithResultAsync(
            null, "retry this draft", QuickCaptureAppearancePreset.Default);

        Assert.True(saved.Saved);
        Assert.NotNull(saved.Item);
        QuickCaptureStoreData reloaded = await new QuickCaptureService(CreateStore()).GetDataAsync();
        QuickCaptureItem added = Assert.Single(reloaded.Items.Where(item => item.Body == "retry this draft"));
        Assert.Equal(saved.Item.Id, added.Id);
        Assert.Equal(2, reloaded.Items.Count);
        Assert.Equal(1, Assert.Single(reloaded.Items.Where(item => item.Body == "existing note")).SortOrder);
    }

    [Fact]
    public async Task ThrowingChangedObserver_DoesNotReportCommittedNoteAsFailed()
    {
        QuickCaptureStore store = CreateStore();
        var service = new QuickCaptureService(store);
        service.Changed += () => throw new InvalidOperationException("observer could not redraw");
        QuickCaptureWriteResult? saved = null;

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            saved = await service.AddDetailedItemWithResultAsync(
                null, "committed note", QuickCaptureAppearancePreset.Default);
        });

        QuickCaptureStoreData reloaded = await new QuickCaptureService(CreateStore()).GetDataAsync();
        Assert.Equal("committed note", Assert.Single(reloaded.Items).Body);
        Assert.Null(failure);
        Assert.NotNull(saved);
        Assert.True(saved.Saved);
        Assert.Equal(reloaded.Items[0].Id, saved.Item!.Id);
    }

    [Fact]
    public async Task ThrowingChangedObserver_DoesNotPreventOtherObserversFromSeeingCommittedNote()
    {
        var service = new QuickCaptureService(CreateStore());
        int completedRefreshes = 0;
        service.Changed += () => throw new InvalidOperationException("one view failed");
        service.Changed += () => completedRefreshes++;

        Exception? failure = await Record.ExceptionAsync(() => service.AddDetailedItemWithResultAsync(
            null, "refresh every subscribed view", QuickCaptureAppearancePreset.Default));

        Assert.Equal(1, completedRefreshes);
        Assert.Null(failure);
        Assert.Single((await new QuickCaptureService(CreateStore()).GetDataAsync()).Items);
    }

    private QuickCaptureStore CreateStore() => new(_root);

    [Fact]
    public async Task CapturedNote_ReplayedOperationIsAcknowledgedWithoutDuplicate()
    {
        var service = new QuickCaptureService(CreateStore());
        QuickCaptureWriteResult first = await service.AddDetailedItemWithResultAsync(
            null, "same draft", QuickCaptureAppearancePreset.Default, operationId: "same-operation");
        QuickCaptureWriteResult second = await service.AddDetailedItemWithResultAsync(
            null, "same draft", QuickCaptureAppearancePreset.Default, operationId: "same-operation");
        Assert.True(first.Saved);
        Assert.True(second.Saved);
        Assert.Equal(first.Item!.Id, second.Item!.Id);
        Assert.Single((await new QuickCaptureService(CreateStore()).GetDataAsync()).Items);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
