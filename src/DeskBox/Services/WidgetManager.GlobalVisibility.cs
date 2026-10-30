using DeskBox.Models;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    public bool GetKeepVisibleOnGlobalHide(WidgetConfig widget) =>
        WidgetGlobalVisibilityPolicy.KeepsVisibleOnGlobalHide(_settingsService.Settings, widget);

    public void SetKeepVisibleOnGlobalHide(WidgetConfig widget, bool keepVisible)
    {
        // Group settings belong to the surface; changing this flag must not
        // overwrite the preferences members will recover when detached.
        _settingsService.MutateGraphUnderLock(() =>
        {
            WidgetGroupConfig? group = WidgetGroupSettings.FindByMember(_settingsService.Settings, widget.Id);
            if (group is not null)
            {
                group.KeepVisibleOnGlobalHide = keepVisible;
            }
            else
            {
                widget.KeepVisibleOnGlobalHide = keepVisible;
            }
        });
        _settingsService.SaveDebounced(notifySubscribers: false);
    }
}
