using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickEntryTests
{
    [Fact]
    public void NewSettings_QuickEntryIsOptIn()
    {
        using JsonDocument document = Serialize(new AppSettings());

        JsonElement enabled = RequireProperty(document.RootElement, "quickEntryHotkeyEnabled");
        Assert.False(enabled.GetBoolean());
    }

    [Fact]
    public void LegacySettings_DefaultGestureIsControlAltN()
    {
        using JsonDocument document = RoundTrip("{}");

        Assert.Equal(
            (int)(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt),
            RequireProperty(document.RootElement, "quickEntryHotkeyModifiers").GetInt32());
        Assert.Equal(0x4E, RequireProperty(document.RootElement, "quickEntryHotkeyKey").GetInt32());
    }

    [Fact]
    public void EnabledQuickEntry_RemainsEnabledAfterSettingsRoundTrip()
    {
        using JsonDocument document = RoundTrip("""{"quickEntryHotkeyEnabled":true}""");

        Assert.True(RequireProperty(document.RootElement, "quickEntryHotkeyEnabled").GetBoolean());
    }

    [Fact]
    public void CustomGesture_PreservesModifiersAndKey()
    {
        using JsonDocument document = RoundTrip(
            """{"quickEntryHotkeyModifiers":6,"quickEntryHotkeyKey":81}""");

        Assert.Equal(6, RequireProperty(document.RootElement, "quickEntryHotkeyModifiers").GetInt32());
        Assert.Equal(81, RequireProperty(document.RootElement, "quickEntryHotkeyKey").GetInt32());
    }

    [Fact]
    public void LastTaskDestination_PreservesStableWidgetId()
    {
        using JsonDocument document = RoundTrip(
            """{"quickEntryLastTodoWidgetId":"todo-collection-stable-id"}""");

        Assert.Equal(
            "todo-collection-stable-id",
            RequireProperty(document.RootElement, "quickEntryLastTodoWidgetId").GetString());
    }

    [Theory]
    [InlineData("Note")]
    [InlineData("Task")]
    public void LastSavedEntryKind_PersistsWithoutChangingClipboardPreferences(string kind)
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["quickEntryLastKind"] = kind,
            ["quickCaptureClipboardEnabled"] = false,
            ["quickCaptureImageClipboardEnabled"] = false
        });
        using JsonDocument document = RoundTrip(json);

        Assert.Equal(kind, RequireProperty(document.RootElement, "quickEntryLastKind").GetString());
        Assert.False(RequireProperty(document.RootElement, "quickCaptureClipboardEnabled").GetBoolean());
        Assert.False(RequireProperty(document.RootElement, "quickCaptureImageClipboardEnabled").GetBoolean());
    }

    private static JsonDocument RoundTrip(string json)
    {
        AppSettings settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;
        return Serialize(settings);
    }

    private static JsonDocument Serialize(AppSettings settings) =>
        JsonDocument.Parse(JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));

    private static JsonElement RequireProperty(JsonElement root, string name)
    {
        Assert.True(root.TryGetProperty(name, out JsonElement value),
            $"Settings persistence must retain the quick-entry property '{name}'.");
        return value;
    }
}
