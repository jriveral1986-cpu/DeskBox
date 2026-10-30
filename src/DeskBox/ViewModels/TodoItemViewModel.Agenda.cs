using Microsoft.UI.Xaml;

namespace DeskBox.ViewModels;

public sealed partial class TodoItemViewModel
{
    private string _agendaSectionText = string.Empty;

    public DateOnly? DailyPriorityDate
    {
        get => _item.DailyPriorityDate;
        internal set
        {
            if (_item.DailyPriorityDate == value) return;
            _item.DailyPriorityDate = value;
            OnPropertyChanged();
        }
    }

    public string AgendaSectionText => _agendaSectionText;

    public Visibility AgendaSectionVisibility => string.IsNullOrEmpty(_agendaSectionText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    internal void SetAgendaSectionHeader(string text)
    {
        if (!SetProperty(ref _agendaSectionText, text, nameof(AgendaSectionText))) return;
        OnPropertyChanged(nameof(AgendaSectionVisibility));
    }
}
