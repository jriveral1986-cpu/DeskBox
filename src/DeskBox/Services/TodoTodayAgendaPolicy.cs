using DeskBox.Models;

namespace DeskBox.Services;

public enum TodoAgendaSection
{
    Priorities,
    Overdue,
    Today,
    Upcoming
}

/// <summary>Calendar-based agenda rules. The caller owns the current local date.</summary>
public static class TodoTodayAgendaPolicy
{
    public const int MaximumDailyPriorities = 3;

    public static bool IsDailyPriority(TodoItem item, DateOnly localDate) =>
        !item.IsCompleted && !item.IsDeleted && item.DailyPriorityDate == localDate;

    public static TodoAgendaSection? GetSection(TodoItem item, DateOnly localDate)
    {
        if (item.IsDeleted)
        {
            return null;
        }

        if (IsDailyPriority(item, localDate))
        {
            return TodoAgendaSection.Priorities;
        }

        if (item.DueDate is not { } dueDate)
        {
            return null;
        }

        DateOnly due = DateOnly.FromDateTime(dueDate.ToLocalTime().DateTime);
        int days = due.DayNumber - localDate.DayNumber;
        return days switch
        {
            < 0 => TodoAgendaSection.Overdue,
            0 => TodoAgendaSection.Today,
            <= 7 => TodoAgendaSection.Upcoming,
            _ => null
        };
    }

    public static int Compare(TodoItem left, TodoItem right, DateOnly localDate)
    {
        int section = Nullable.Compare(GetSection(left, localDate), GetSection(right, localDate));
        if (section != 0) return section;
        int completed = left.IsCompleted.CompareTo(right.IsCompleted);
        if (completed != 0) return completed;
        int due = Nullable.Compare(left.DueDate, right.DueDate);
        if (due != 0) return due;
        int order = left.SortOrder.CompareTo(right.SortOrder);
        return order != 0 ? order : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }
}
