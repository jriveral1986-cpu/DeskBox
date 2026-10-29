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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControllerQueue_TwoRequestsRestoreNormalPanelsButNeverRevealHiddenExemptPanel(bool quickReveal)
    {
        var harness = new VisibilityHarness(quickReveal);
        Surface normal = harness.Add("normal", visible: true);
        Surface exempt = harness.Add("exempt", visible: true, keepVisible: true);
        Surface hiddenExempt = harness.Add("hidden-exempt", visible: false, keepVisible: true);
        var firstHideApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstHide = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AfterApplyAsync = async operation =>
        {
            if (harness.Operations.Count == 1)
            {
                firstHideApplied.TrySetResult();
                await releaseFirstHide.Task;
            }
        };

        Task first = harness.Queue.EnqueueAsync("hotkey");
        await firstHideApplied.Task;
        Assert.False(normal.Visible);
        Assert.True(exempt.Visible);
        Task second = harness.Queue.EnqueueAsync("hotkey");
        releaseFirstHide.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.True(normal.Visible);
        Assert.True(exempt.Visible);
        Assert.False(hiddenExempt.Visible);
        Assert.Equal([false, true], harness.Operations.Select(operation => operation.Visible));
        Assert.All(harness.Operations, operation => Assert.Equal(["normal"], operation.WidgetIds.ToArray()));
    }

    [Fact]
    public async Task ControllerQueue_AllExemptIsANoOpForVisibleAndHiddenSurfaces()
    {
        var harness = new VisibilityHarness(quickReveal: false);
        Surface visible = harness.Add("visible", visible: true, keepVisible: true);
        Surface hidden = harness.Add("hidden", visible: false, keepVisible: true);

        await harness.Queue.EnqueueAsync("hotkey");
        await harness.Queue.EnqueueAsync("desktop-double-click");

        Assert.True(visible.Visible);
        Assert.False(hidden.Visible);
        Assert.Empty(harness.Operations);
    }

    [Fact]
    public async Task ControllerQueue_QuickRevealOutsideDismissHidesRetainedSurfaceAndEndsMonitor()
    {
        var harness = new VisibilityHarness(quickReveal: true);
        Surface normal = harness.Add("normal", visible: true);
        Surface exempt = harness.Add("exempt", visible: true, keepVisible: true);

        await harness.Queue.EnqueueAsync("hotkey");

        Assert.False(normal.Visible);
        Assert.True(exempt.Visible);
        Assert.True(harness.RaisedSession);
        Assert.True(harness.OutsideDismissMonitorActive);

        await harness.DismissOutsideAsync();

        Assert.False(exempt.Visible);
        Assert.False(harness.RaisedSession);
        Assert.False(harness.OutsideDismissMonitorActive);
        Assert.False(harness.Operations[^1].UserGlobalToggle);
        Assert.Equal(["exempt"], harness.Operations[^1].WidgetIds.ToArray());
    }

    [Fact]
    public async Task ControllerQueue_DoesNotRevealDisabledOrDisconnectedSurfaces()
    {
        var harness = new VisibilityHarness(quickReveal: false);
        Surface normal = harness.Add("normal", visible: false);
        Surface disabled = harness.Add("disabled", visible: false, disabled: true);
        Surface disconnected = harness.Add("disconnected", visible: false, available: false);

        await harness.Queue.EnqueueAsync("hotkey");

        Assert.True(normal.Visible);
        Assert.False(disabled.Visible);
        Assert.False(disconnected.Visible);
        Assert.Equal(["normal"], Assert.Single(harness.Operations).WidgetIds.ToArray());
    }

    [Fact]
    public async Task ControllerQueue_GroupSurfaceFlagDoesNotMutateMemberPreferences()
    {
        var harness = new VisibilityHarness(quickReveal: false);
        Surface normal = harness.Add("normal", visible: true);
        Surface active = harness.Add("a", visible: true);
        Surface inactive = harness.Add("b", visible: false, keepVisible: true);
        harness.Settings.WidgetLayout.WidgetGroups =
        [new() { MemberIds = ["a", "b"], ActiveMemberId = "a", KeepVisibleOnGlobalHide = true }];

        await harness.Queue.EnqueueAsync("hotkey");
        Assert.False(normal.Visible);
        Assert.True(active.Visible);
        Assert.False(inactive.Visible);
        Assert.False(active.Config.KeepVisibleOnGlobalHide);
        Assert.True(inactive.Config.KeepVisibleOnGlobalHide);
    }

    private sealed class Surface(WidgetConfig config, bool visible, bool available)
    {
        public WidgetConfig Config { get; } = config;
        public bool Visible { get; set; } = visible;
        public bool Available { get; } = available;
    }

    // Uses the same operation owner and serialized queue as WidgetManager.
    // Only native window/animation and monitor effects are substituted here.
    private sealed class VisibilityHarness
    {
        private readonly bool _quickReveal;
        private readonly List<Surface> _surfaces = [];
        public AppSettings Settings { get; } = new();
        public List<WidgetGlobalVisibilityOperation> Operations { get; } = [];
        public TrayToggleRequestQueue Queue { get; }
        public bool RaisedSession { get; private set; }
        public bool OutsideDismissMonitorActive { get; private set; }
        public Func<WidgetGlobalVisibilityOperation, Task>? AfterApplyAsync { get; set; }

        public VisibilityHarness(bool quickReveal)
        {
            _quickReveal = quickReveal;
            RaisedSession = quickReveal;
            OutsideDismissMonitorActive = quickReveal;
            Queue = new TrayToggleRequestQueue(_ => WidgetGlobalVisibilityController.ToggleAsync(
                Settings, Capture(), new TrayToggleDecisionContext(
                    IsDesktopPinnedMode: !_quickReveal, IsQuickRevealMode: _quickReveal,
                    IsRaisedSession: RaisedSession, HasVisibleWidgets: _surfaces.Any(surface => surface.Visible),
                    IsForegroundLocal: true), ApplyAsync));
        }

        public Surface Add(string id, bool visible, bool keepVisible = false, bool disabled = false, bool available = true)
        {
            var config = new WidgetConfig
            {
                Id = id, KeepVisibleOnGlobalHide = keepVisible, IsVisible = visible, IsDisabled = disabled
            };
            Settings.WidgetLayout.Widgets.Add(config);
            var surface = new Surface(config, visible, available);
            _surfaces.Add(surface);
            return surface;
        }

        public Task DismissOutsideAsync() => WidgetGlobalVisibilityController.SetVisibleAsync(
            Settings, Capture(), visible: false, userGlobalToggle: false, ApplyAsync);

        private IReadOnlyList<WidgetGlobalVisibilitySurface> Capture() => _surfaces
            .Select(surface => new WidgetGlobalVisibilitySurface(
                surface.Config, surface.Visible, surface.Available)).ToList();

        private async Task ApplyAsync(WidgetGlobalVisibilityOperation operation)
        {
            Operations.Add(operation);
            foreach (Surface surface in _surfaces.Where(surface => operation.WidgetIds.Contains(surface.Config.Id)))
            {
                surface.Visible = operation.Visible;
            }
            RaisedSession = operation.Visible
                ? _quickReveal && _surfaces.Any(surface => surface.Visible)
                : WidgetGlobalVisibilityController.KeepQuickRevealSessionAfterHide(
                    operation.UserGlobalToggle, _quickReveal, Capture());
            OutsideDismissMonitorActive = _quickReveal && RaisedSession;
            if (AfterApplyAsync is { } afterApply)
            {
                await afterApply(operation);
            }
        }
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
