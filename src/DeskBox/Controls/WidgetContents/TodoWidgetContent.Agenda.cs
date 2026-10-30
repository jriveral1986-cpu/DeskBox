using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class TodoWidgetContent
{
    private readonly DispatcherTimer _agendaDateTimer = new()
    {
        Interval = TimeSpan.FromSeconds(15)
    };

    private void StartAgendaClock()
    {
        ViewModel?.RefreshAgendaDate();
        _agendaDateTimer.Tick -= AgendaDateTimer_Tick;
        _agendaDateTimer.Tick += AgendaDateTimer_Tick;
        _agendaDateTimer.Start();
    }

    internal void StopAgendaClock()
    {
        _agendaDateTimer.Stop();
        _agendaDateTimer.Tick -= AgendaDateTimer_Tick;
    }

    private void AgendaDateTimer_Tick(object? sender, object e)
    {
        // The dispatcher resumes after sleep; refreshing from the current date
        // handles missed midnights without reloading stores or editor state.
        ViewModel?.RefreshAgendaDate();
    }

    private MenuFlyoutItem CreateDailyPriorityItem(TodoItemViewModel item, MenuFlyout flyout)
    {
        ViewModel?.RefreshAgendaDate();
        bool selected = ViewModel?.IsDailyPriority(item) == true;
        MenuFlyoutItem command = CreateTodoContextCommand(
            selected ? "Todo.Agenda.RemovePriority" : "Todo.Agenda.AddPriority", "\uE734");
        command.IsEnabled = ViewModel is not null &&
            (selected || ViewModel.CanSelectDailyPriority(item));
        if (!command.IsEnabled && !item.IsCompleted)
        {
            ToolTipService.SetToolTip(command, App.Current.LocalizationService.T("Todo.Agenda.PriorityLimit"));
        }
        command.Click += async (_, _) =>
        {
            flyout.Hide();
            if (ViewModel is null) return;
            if (!await ViewModel.SetDailyPriorityAsync(item.Id, !selected))
            {
                FeedbackRequested?.Invoke(this, new WidgetFeedbackRequestedEventArgs(
                    new WidgetFeedbackRequest(
                        App.Current.LocalizationService.T("Todo.Agenda.PrioritySaveFailed"),
                        WidgetFeedbackSeverity.Error,
                        "todo-priority")));
            }
        };
        return command;
    }
}
