using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetKeepVisibleTests
{
    [Fact]
    public async Task ManagerPreferenceChange_ChangesOnlyTheOwningSurfaceAndDoesNotRevealHiddenPanel()
    {
        string root = Path.Combine(Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = new SettingsService(Path.Combine(root, "settings"));
        var files = new FileService();
        var manager = new WidgetManager(
            settings, files, TestOrganizerServices.Create(settings, files),
            new ThemeService(settings),
            new QuickCaptureService(new QuickCaptureStore(Path.Combine(root, "quick-capture"))),
            () => root, recycleManagedFolderDeletes: false);
        try
        {
            var panel = new WidgetConfig { Id = "a", IsVisible = false };
            var other = new WidgetConfig { Id = "b", KeepVisibleOnGlobalHide = true };
            settings.Settings.WidgetLayout.Widgets = [panel, other];
            var group = new WidgetGroupConfig { MemberIds = ["a", "b"], ActiveMemberId = "a" };
            settings.Settings.WidgetLayout.WidgetGroups = [group];

            manager.SetKeepVisibleOnGlobalHide(panel, true);

            Assert.True(manager.GetKeepVisibleOnGlobalHide(panel));
            Assert.True(group.KeepVisibleOnGlobalHide);
            Assert.False(panel.KeepVisibleOnGlobalHide);
            Assert.True(other.KeepVisibleOnGlobalHide);
            Assert.False(panel.IsVisible);

            settings.Settings.WidgetLayout.WidgetGroups.Clear();
            Assert.False(manager.GetKeepVisibleOnGlobalHide(panel));
            manager.SetKeepVisibleOnGlobalHide(panel, true);
            Assert.True(panel.KeepVisibleOnGlobalHide);
            Assert.False(panel.IsVisible);
        }
        finally
        {
            await settings.FlushPendingSaveAsync(notifySubscribers: false);
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, false)]
    public void HidePolicy_ExemptsVisiblePanelsOnlyForUserGlobalHide(
        bool keepVisible,
        bool currentlyVisible,
        bool userGlobalHide,
        bool expected)
    {
        var panel = new WidgetConfig { KeepVisibleOnGlobalHide = keepVisible };
        var settings = new AppSettings { Widgets = [panel] };

        Assert.Equal(expected, WidgetGlobalVisibilityPolicy.ShouldHide(
            settings, panel, currentlyVisible, userGlobalHide));
    }

    [Fact]
    public void ToggleParticipation_ExcludesProtectedDisabledAndInactiveMembers()
    {
        var normal = new WidgetConfig { Id = "normal" };
        var protectedPanel = new WidgetConfig { Id = "protected", KeepVisibleOnGlobalHide = true };
        var disabled = new WidgetConfig { Id = "disabled", IsDisabled = true };
        var deleted = new WidgetConfig { Id = "deleted" };
        var inactive = new WidgetConfig { Id = "inactive" };
        var settings = new AppSettings
        {
            Widgets = [normal, protectedPanel, disabled, deleted, inactive],
            DeletedWidgetIds = ["deleted"],
            WidgetGroups = [new() { MemberIds = ["normal", "inactive"], ActiveMemberId = "normal" }]
        };

        Assert.True(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, normal));
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, protectedPanel));
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, disabled));
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, deleted));
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, inactive));
    }

    [Fact]
    public void GroupPreference_OverridesMembersUntilGroupIsDissolved()
    {
        var active = new WidgetConfig { Id = "a", KeepVisibleOnGlobalHide = true };
        var inactive = new WidgetConfig { Id = "b" };
        var group = new WidgetGroupConfig
        {
            MemberIds = ["a", "b"], ActiveMemberId = "a", KeepVisibleOnGlobalHide = false
        };
        var settings = new AppSettings { Widgets = [active, inactive], WidgetGroups = [group] };

        Assert.True(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, active));
        group.KeepVisibleOnGlobalHide = true;
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, active));
        Assert.False(WidgetGlobalVisibilityPolicy.ShouldHide(settings, active, true, true));
        Assert.True(WidgetGlobalVisibilityPolicy.ShouldHide(settings, active, true, false));

        settings.WidgetGroups.Clear();
        Assert.False(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, active));
        Assert.True(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, inactive));
        Assert.True(active.KeepVisibleOnGlobalHide);
        Assert.False(inactive.KeepVisibleOnGlobalHide);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleDecision_AfterNormalPanelsHide_ProtectedPanelsDoNotTrapToggle(bool quickReveal)
    {
        var normal = new WidgetConfig { Id = "normal" };
        var protectedPanel = new WidgetConfig { Id = "protected", KeepVisibleOnGlobalHide = true };
        var settings = new AppSettings { Widgets = [normal, protectedPanel] };
        WidgetConfig[] visiblePanels = [protectedPanel];
        bool hasVisibleParticipants = visiblePanels.Any(panel =>
            WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, panel));

        Assert.False(TrayToggleDecisionPolicy.ShouldHide(new TrayToggleDecisionContext(
            IsDesktopPinnedMode: !quickReveal,
            IsQuickRevealMode: quickReveal,
            IsRaisedSession: false,
            HasVisibleWidgets: hasVisibleParticipants,
            IsForegroundLocal: true)));
        Assert.True(WidgetGlobalVisibilityPolicy.IsGlobalToggleParticipant(settings, normal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettingsRoundTrip_PreservesPanelGlobalHidePreference(bool keepVisible)
    {
        string json = $$"""
            {"widgets":[{"id":"panel","keepVisibleOnGlobalHide":{{keepVisible.ToString().ToLowerInvariant()}}}]}
            """;

        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        using JsonDocument saved = JsonDocument.Parse(
            JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        JsonElement panel = saved.RootElement.GetProperty("widgets")[0];

        Assert.True(panel.TryGetProperty("keepVisibleOnGlobalHide", out JsonElement preference),
            "The global-hide preference must survive a settings save and restart.");
        Assert.Equal(keepVisible, preference.GetBoolean());
    }

    [Fact]
    public void LegacySettings_PanelsStillParticipateInGlobalHideByDefault()
    {
        const string json = """{"widgets":[{"id":"legacy-panel"}]}""";
        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        using JsonDocument saved = JsonDocument.Parse(
            JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));

        JsonElement panel = saved.RootElement.GetProperty("widgets")[0];
        Assert.True(panel.TryGetProperty("keepVisibleOnGlobalHide", out JsonElement preference));
        Assert.False(preference.GetBoolean());
        Assert.True(settings.Widgets[0].IsVisible);
    }

    [Fact]
    public void SettingsRoundTrip_PreservesHiddenAndDisabledStateOfExemptPanel()
    {
        const string json = """
            {"widgets":[{"id":"hidden","keepVisibleOnGlobalHide":true,"isVisible":false,"isDisabled":true}]}
            """;
        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        using JsonDocument saved = JsonDocument.Parse(
            JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));

        JsonElement panel = saved.RootElement.GetProperty("widgets")[0];
        Assert.True(panel.TryGetProperty("keepVisibleOnGlobalHide", out JsonElement preference));
        Assert.True(preference.GetBoolean());
        Assert.False(settings.Widgets[0].IsVisible);
        Assert.True(settings.Widgets[0].IsDisabled);
    }

    [Fact]
    public void GroupNormalizationAndRoundTrip_KeepSurfaceAndMemberPreferencesIndependent()
    {
        const string json = """
            {
              "widgets":[
                {"id":"a","keepVisibleOnGlobalHide":false},
                {"id":"b","keepVisibleOnGlobalHide":true}
              ],
              "widgetGroups":[{
                "id":"group","memberIds":["a","b"],"activeMemberId":"a",
                "keepVisibleOnGlobalHide":true
              }]
            }
            """;
        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        WidgetGroupSettings.Normalize(settings);
        using JsonDocument saved = JsonDocument.Parse(
            JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));

        JsonElement group = saved.RootElement.GetProperty("widgetGroups")[0];
        Assert.True(group.TryGetProperty("keepVisibleOnGlobalHide", out JsonElement groupPreference));
        Assert.True(groupPreference.GetBoolean());
        JsonElement members = saved.RootElement.GetProperty("widgets");
        Assert.False(members[0].GetProperty("keepVisibleOnGlobalHide").GetBoolean());
        Assert.True(members[1].GetProperty("keepVisibleOnGlobalHide").GetBoolean());
        Assert.Equal(["a", "b"], settings.WidgetGroups[0].MemberIds);
    }
}
