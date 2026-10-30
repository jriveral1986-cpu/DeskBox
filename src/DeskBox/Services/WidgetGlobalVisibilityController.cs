using DeskBox.Models;

namespace DeskBox.Services;

internal sealed record WidgetGlobalVisibilitySurface(
    WidgetConfig Config,
    bool Visible,
    bool AvailableForSession);

internal sealed record WidgetGlobalVisibilityOperation(
    bool Visible,
    bool UserGlobalToggle,
    IReadOnlySet<string> WidgetIds);

/// <summary>
/// Plans and dispatches one serialized visibility operation. Window effects
/// stay in WidgetManager; selection and user/system intent have one owner.
/// </summary>
internal static class WidgetGlobalVisibilityController
{
    public static async Task ToggleAsync(
        AppSettings settings,
        IReadOnlyList<WidgetGlobalVisibilitySurface> surfaces,
        TrayToggleDecisionContext context,
        Func<WidgetGlobalVisibilityOperation, Task> applyAsync)
    {
        WidgetGlobalVisibilityOperation? operation = CreateToggleOperation(settings, surfaces, context);
        if (operation is not null)
        {
            await applyAsync(operation);
        }
    }

    public static WidgetGlobalVisibilityOperation? CreateToggleOperation(
        AppSettings settings,
        IReadOnlyList<WidgetGlobalVisibilitySurface> surfaces,
        TrayToggleDecisionContext context)
    {
        var participants = surfaces.Where(surface =>
            surface.AvailableForSession &&
            WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, surface.Config)).ToList();
        if (participants.Count == 0)
        {
            return null;
        }

        bool anyVisible = participants.Any(surface => surface.Visible);
        bool hide = TrayToggleDecisionPolicy.ShouldHide(context with
        {
            HasVisibleWidgets = anyVisible,
            IsRaisedSession = context.IsRaisedSession && anyVisible
        });
        return CreateOperation(settings, surfaces, !hide, userGlobalToggle: true);
    }

    public static Task SetVisibleAsync(
        AppSettings settings,
        IReadOnlyList<WidgetGlobalVisibilitySurface> surfaces,
        bool visible,
        bool userGlobalToggle,
        Func<WidgetGlobalVisibilityOperation, Task> applyAsync)
    {
        WidgetGlobalVisibilityOperation operation = CreateOperation(settings, surfaces, visible, userGlobalToggle);
        return applyAsync(operation);
    }

    public static WidgetGlobalVisibilityOperation CreateOperation(
        AppSettings settings,
        IReadOnlyList<WidgetGlobalVisibilitySurface> surfaces,
        bool visible,
        bool userGlobalToggle)
    {
        IEnumerable<WidgetGlobalVisibilitySurface> targets = surfaces;
        if (userGlobalToggle)
        {
            targets = targets.Where(surface => surface.AvailableForSession &&
                WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, surface.Config));
        }
        else if (visible)
        {
            targets = targets.Where(surface => surface.AvailableForSession);
        }
        if (!visible)
        {
            targets = targets.Where(surface => surface.Visible);
        }
        return new WidgetGlobalVisibilityOperation(visible, userGlobalToggle,
            targets.Select(surface => surface.Config.Id).ToHashSet(StringComparer.Ordinal));
    }

    public static bool KeepQuickRevealSessionAfterHide(
        bool userGlobalToggle,
        bool quickRevealMode,
        IReadOnlyList<WidgetGlobalVisibilitySurface> remainingSurfaces) =>
        userGlobalToggle && quickRevealMode && remainingSurfaces.Any(surface => surface.Visible);
}
