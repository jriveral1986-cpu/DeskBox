using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Resolves the user hide exception at surface level. System cleanup never
/// uses the exception, and grouping does not overwrite member preferences.
/// </summary>
internal static class WidgetGlobalVisibilityPolicy
{
    public static bool KeepsVisibleOnGlobalHide(AppSettings settings, WidgetConfig widget)
    {
        return WidgetGroupSettings.FindByMember(settings, widget.Id)?.KeepVisibleOnGlobalHide
            ?? widget.KeepVisibleOnGlobalHide;
    }

    public static bool IsGlobalToggleParticipant(AppSettings settings, WidgetConfig widget)
    {
        return !widget.IsDisabled &&
               !settings.WidgetLayout.DeletedWidgetIds.Contains(widget.Id, StringComparer.Ordinal) &&
               WidgetGroupSettings.IsActiveMember(settings, widget.Id) &&
               !KeepsVisibleOnGlobalHide(settings, widget);
    }

    public static bool ShouldHide(
        AppSettings settings,
        WidgetConfig widget,
        bool currentlyVisible,
        bool userGlobalHide)
    {
        return currentlyVisible &&
               (!userGlobalHide || !KeepsVisibleOnGlobalHide(settings, widget));
    }
}
