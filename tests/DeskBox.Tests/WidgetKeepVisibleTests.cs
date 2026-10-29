using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetKeepVisibleTests
{
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
