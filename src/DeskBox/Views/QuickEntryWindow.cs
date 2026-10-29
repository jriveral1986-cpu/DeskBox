using DeskBox.Models;
using DeskBox.Helpers;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace DeskBox.Views;

public sealed record QuickEntryDestination(string Id, string Name);

/// <summary>A single reusable entry surface; controls are explicit to retain Native AOT support.</summary>
public sealed class QuickEntryWindow : Window, IDisposable
{
    private readonly QuickEntrySession _draft;
    private readonly LocalizationService _localization;
    private readonly ThemeService _theme;
    private readonly Func<IReadOnlyList<QuickEntryDestination>> _destinations;
    private readonly Func<QuickEntryRequest, Task<QuickEntrySaveResult>> _save;
    private readonly Func<Task<string?>> _createTodo;
    private readonly StackPanel _root = new() { Padding = new Thickness(24), Spacing = 12 };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox _kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _destination = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 210 };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _saveButton = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _createButton = new();
    private bool _refreshing;
    private bool _composing;
    private bool _disposed;
    private bool _visible;
    private int _presentationGeneration;
    private IntPtr _previousForeground;

    public QuickEntryWindow(QuickEntrySession draft, LocalizationService localization, ThemeService theme,
        Func<IReadOnlyList<QuickEntryDestination>> destinations,
        Func<QuickEntryRequest, Task<QuickEntrySaveResult>> save, Func<Task<string?>> createTodo)
    {
        _draft = draft; _localization = localization; _theme = theme;
        _destinations = destinations; _save = save; _createTodo = createTodo;
        _scroll.Content = _root;
        Content = _scroll;
        _root.Children.Add(_kind); _root.Children.Add(_destination); _root.Children.Add(_text);
        _root.Children.Add(_hint); _root.Children.Add(_error); _root.Children.Add(_createButton); _root.Children.Add(_saveButton);
        _text.Text = draft.Text;
        _text.TextChanged += (_, _) => { _draft.Text = _text.Text; UpdateSaveState(); };
        _text.TextCompositionStarted += (_, _) => _composing = true;
        _text.TextCompositionEnded += (_, _) =>
        {
            // Keep the IME commit Enter out of the submit path in this input turn.
            DispatcherQueue.TryEnqueue(() => _composing = false);
        };
        _root.PreviewKeyDown += OnKeyDown;
        _kind.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _draft.Kind = _kind.SelectedIndex == 1 ? "Task" : "Note";
            RefreshDestinations();
        };
        _destination.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _destination.SelectedItem is ComboBoxItem item && item.Tag is string id)
                _draft.TodoWidgetId = id;
            UpdateSaveState();
        };
        _saveButton.Click += async (_, _) => await SaveAsync();
        _createButton.Click += async (_, _) => await CreateDestinationAsync();
        AppWindow.Closing += (_, args) => { if (!_disposed) { args.Cancel = true; Hide(); } };
        _theme.AppearanceChanged += OnAppearanceChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshLabels();
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsResizable = true;
    }

    public void ShowEntry()
    {
        if (_disposed) return;
        _presentationGeneration++;
        if (!_visible) _previousForeground = Win32Helper.GetForegroundWindow();
        _error.Text = string.Empty;
        RefreshDestinations();
        _text.Text = _draft.Text;
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 work = area.WorkArea;
        double scale = Win32Helper.GetDpiScaleForWindow(WindowNative.GetWindowHandle(this), _root.XamlRoot);
        int width = Math.Min(work.Width, (int)Math.Ceiling(520 * scale));
        int height = Math.Min(work.Height, (int)Math.Ceiling(490 * scale));
        AppWindow.MoveAndResize(new RectInt32(work.X + Math.Max(0, (work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2), width, height));
        Activate();
        _visible = true;
        _text.Focus(FocusState.Programmatic);
        _text.SelectionStart = _text.Text.Length;
        Win32Helper.SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    private void Hide()
    {
        _presentationGeneration++;
        _draft.Text = _text.Text;
        AppWindow.Hide(); _visible = false;
    }

    private void RefreshLabels()
    {
        _refreshing = true;
        Title = _localization.T("QuickEntry.Title");
        _kind.Items.Clear();
        _kind.Items.Add(new ComboBoxItem { Content = _localization.T("QuickEntry.Note") });
        _kind.Items.Add(new ComboBoxItem { Content = _localization.T("QuickEntry.Task") });
        _kind.SelectedIndex = _draft.Kind == "Task" ? 1 : 0;
        _text.PlaceholderText = _localization.T("QuickEntry.Placeholder");
        _hint.Text = _localization.T("QuickEntry.Keys");
        _saveButton.Content = _localization.T("Common.Save");
        _createButton.Content = _localization.T("QuickEntry.CreateTodo");
        _destination.PlaceholderText = _localization.T("QuickEntry.ChooseTodo");
        AutomationProperties.SetName(_kind, _localization.T("QuickEntry.Kind"));
        AutomationProperties.SetName(_destination, _localization.T("QuickEntry.ChooseTodo"));
        AutomationProperties.SetName(_text, _localization.T("QuickEntry.Placeholder"));
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        ApplyAppearance();
        _refreshing = false;
        RefreshDestinations();
    }

    private void RefreshDestinations()
    {
        bool task = _draft.Kind == "Task";
        _refreshing = true;
        _destination.Items.Clear();
        foreach (QuickEntryDestination target in _destinations())
        {
            var item = new ComboBoxItem { Content = target.Name, Tag = target.Id };
            _destination.Items.Add(item);
            if (target.Id == _draft.TodoWidgetId) _destination.SelectedItem = item;
        }
        _destination.Visibility = task ? Visibility.Visible : Visibility.Collapsed;
        _createButton.Visibility = task && _destination.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _refreshing = false;
        if (task && _destination.Items.Count == 0) _error.Text = _localization.T("QuickEntry.NoTodo");
        UpdateSaveState();
    }

    private void UpdateSaveState() => _saveButton.IsEnabled = !_draft.IsSaving &&
        !string.IsNullOrWhiteSpace(_draft.Text) && (_draft.Kind == "Note" || _destination.SelectedItem is ComboBoxItem);

    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_composing || _disposed) return;
        if (args.Key == VirtualKey.Escape) { args.Handled = true; if (!_draft.IsSaving) Hide(); return; }
        if (args.Key != VirtualKey.Enter || Win32Helper.IsKeyDown((int)VirtualKey.Shift)) return;
        // Buttons and combo boxes retain their own Enter activation.
        if (_text.FocusState == FocusState.Unfocused) return;
        args.Handled = true;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        if (_draft.IsSaving || !_saveButton.IsEnabled) return;
        _draft.Text = _text.Text;
        _saveButton.IsEnabled = false; _text.IsEnabled = false; _kind.IsEnabled = false; _destination.IsEnabled = false; _createButton.IsEnabled = false;
        try
        {
            QuickEntrySaveResult result = await _draft.SaveAsync(_save);
            if (_disposed) return;
            if (!result.Saved)
            { _error.Text = _localization.T(result.Error ?? "QuickEntry.SaveFailed"); return; }
            _text.Text = _draft.Text;
            Hide();
            if (_previousForeground != IntPtr.Zero && Win32Helper.IsWindow(_previousForeground))
                Win32Helper.SetForegroundWindow(_previousForeground);
        }
        catch (Exception ex)
        {
            App.Log($"[QuickEntry] Draft retained after failed save: {ex.Message}");
            _error.Text = _localization.T("QuickEntry.SaveFailed");
        }
        finally
        {
            if (!_disposed)
            {
                _text.IsEnabled = true; _kind.IsEnabled = true; _destination.IsEnabled = true; _createButton.IsEnabled = true;
                UpdateSaveState();
            }
        }
    }

    private async Task CreateDestinationAsync()
    {
        if (_disposed || !_visible) return;
        int presentation = _presentationGeneration;
        _createButton.IsEnabled = false;
        try
        {
            string? id = await _createTodo();
            if (_disposed || !_visible || presentation != _presentationGeneration) return;
            if (id is not null) { _draft.TodoWidgetId = id; _error.Text = string.Empty; RefreshDestinations(); Activate(); _text.Focus(FocusState.Programmatic); }
            else _error.Text = _localization.T("QuickEntry.NoTodo");
        }
        catch (Exception ex)
        {
            App.Log($"[QuickEntry] Destination creation failed: {ex.Message}");
            if (!_disposed && _visible && presentation == _presentationGeneration)
                _error.Text = _localization.T("QuickEntry.SaveFailed");
        }
        finally { if (!_disposed) _createButton.IsEnabled = true; }
    }

    private void ApplyAppearance()
    {
        _root.RequestedTheme = _theme.CurrentTheme;
        _scroll.RequestedTheme = _theme.CurrentTheme;
        bool dark = _theme.CurrentTheme == ElementTheme.Dark ||
            (_theme.CurrentTheme == ElementTheme.Default && Win32Helper.IsSystemDarkMode());
        Brush background = NeutralInteractionBrush.ResolveThemedResource("ApplicationPageBackgroundThemeBrush", _root) ??
            new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 250, 250, 250));
        _root.Background = background; _scroll.Background = background;
    }
    private void OnAppearanceChanged() => DispatcherQueue.TryEnqueue(() => { if (!_disposed) ApplyAppearance(); });
    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() => { if (!_disposed) RefreshLabels(); });
    public void Dispose()
    {
        if (_disposed) return;
        _presentationGeneration++;
        _disposed = true;
        _theme.AppearanceChanged -= OnAppearanceChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        Close();
    }
}
