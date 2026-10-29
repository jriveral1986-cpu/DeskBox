using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
using DeskBox.ViewModels;
using DeskBox.Views;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    internal IReadOnlyList<QuickEntryDestination> GetQuickEntryDestinations() =>
        _settingsService.Settings.WidgetLayout.Widgets.Where(config => IsQuickEntryTodoAvailable(config))
            .Select(config => new QuickEntryDestination(config.Id,
                config.IsDefaultTitle ? _localizationService.T("Todo.Title") : config.Name)).ToList();

    private bool IsQuickEntryTodoAvailable(WidgetConfig config) =>
        config.WidgetKind == WidgetKind.Todo && !config.IsDisabled && !IsDeleted(config.Id) &&
        GetFeatureWidgetEnabledState(WidgetKind.Todo);

    internal async Task<bool> AddQuickEntryTaskAsync(string widgetId, string operationId, string text)
    {
        SemaphoreSlim featureGate = _featureWidgetUpdateLocks[WidgetKind.Todo];
        await featureGate.WaitAsync();
        SemaphoreSlim? surfaceGate = null;
        IDisposable? contentLease = null;
        try
        {
            WidgetConfig? config = _settingsService.Settings.WidgetLayout.Widgets.FirstOrDefault(item => item.Id == widgetId);
            if (config is null || !IsQuickEntryTodoAvailable(config)) return false;
            WidgetGroupConfig? group = WidgetGroupSettings.FindByMember(_settingsService.Settings, widgetId);
            if (group is not null)
            {
                surfaceGate = GetWidgetSurfaceSwitchGate(group);
                await surfaceGate.WaitAsync();
            }
            // Resolve after acquiring the group's switching lane, so inactive
            // cached content cannot be evicted/disposed during this save.
            TodoWidgetViewModel? owner = FindQuickEntryTodoOwner(widgetId);
            if (owner is not null)
            {
                ContentWidgetWindow? owningWindow = _contentWidgets.Values.Distinct().FirstOrDefault(window =>
                    window.CurrentContent is TodoWidgetContentAdapter current && current.WidgetId == widgetId ||
                    window.PeekCachedGroupContent(widgetId) is TodoWidgetContentAdapter);
                contentLease = owningWindow?.RetainContentForQuickEntry();
            }
            if (owner is null)
            {
                if (group is not null && group.ActiveMemberId != widgetId)
                {
                    // No live/cached owner exists for this inactive member. The
                    // group's switch lane excludes creation of a second owner
                    // until this short-lived owner has durably saved and disposed.
                    // Do not reveal the group or switch its active tab to capture.
                    ContentWidgetWindowPlan plan = CreateSurfaceContentWindowFactory().CreateContentWindowPlan(config);
                    if (plan.Content is not TodoWidgetContentAdapter detached) return false;
                    using (detached)
                    {
                        await detached.InitializeAsync();
                        return await detached.ViewModel.AddCapturedItemAsync(operationId, text) is not null;
                    }
                }
                ContentWidgetWindow window = await CreateContentWidgetFromConfigAsync(config,
                    keepPreparedForAnimation: true, showRaisedWhileInitializing: false);
                await window.ContentReadyTask;
                contentLease = window.RetainContentForQuickEntry();
                owner = FindQuickEntryTodoOwner(widgetId);
            }
            if (owner is null || !IsQuickEntryTodoAvailable(config)) return false;
            if (!owner.IsInitialized) await owner.InitializeAsync();
            return await owner.AddCapturedItemAsync(operationId, text) is not null;
        }
        finally
        {
            contentLease?.Dispose();
            surfaceGate?.Release();
            featureGate.Release();
        }
    }

    private TodoWidgetViewModel? FindQuickEntryTodoOwner(string widgetId)
    {
        foreach (ContentWidgetWindow window in _contentWidgets.Values.Distinct())
        {
            if (window.CurrentContent is TodoWidgetContentAdapter current && current.WidgetId == widgetId)
                return current.ViewModel;
            if (window.PeekCachedGroupContent(widgetId) is TodoWidgetContentAdapter cached)
                return cached.ViewModel;
        }
        return null;
    }
}
