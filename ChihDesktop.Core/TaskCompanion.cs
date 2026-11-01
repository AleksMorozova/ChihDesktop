using System.Globalization;

namespace ChihDesktop;

public static class TaskReminderTime
{
    // An offset-less local input is interpreted only in the explicitly selected zone.
    // DST gaps and overlaps require the user to choose a different unambiguous time.
    public static DateTimeOffset Resolve(string local, string timeZoneId)
    {
        if (!DateTime.TryParseExact(local, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) throw new ArgumentException("Дата: ГГГГ-ММ-ДД и время ЧЧ:ММ.");
        var wall = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        var zone = AppTimeZones.Get(timeZoneId);
        if (zone.IsInvalidTime(wall) || zone.IsAmbiguousTime(wall))
            throw new ArgumentException("Это время пропущено или повторяется при переводе часов. Выберите другое время.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone));
    }
}

public static class TaskCompanionPolicy
{
    public static bool MayDeliver(UserSettings settings, bool quiet, bool blocked) =>
        settings.ReminderNotificationsEnabled && settings.TaskReminderNotificationsEnabled && !quiet && !blocked;

    public static PlannerTaskDto? Next(IEnumerable<PlannerTaskDto> tasks, DateTimeOffset now) => tasks
        .Where(t => t.Status == PlannerTaskStatus.Active && t.ReminderStatus == TaskReminderStatus.Pending &&
            (t.SnoozedUntilUtc ?? t.RemindAtUtc) is { } due && due <= now)
        .OrderBy(t => t.SnoozedUntilUtc ?? t.RemindAtUtc).ThenBy(t => t.Id).FirstOrDefault();

    public static string BubbleTitle(PlannerTaskDto task, UserSettings settings) =>
        settings.ShowTaskTitlesInBubbles ? task.Title : "Личная задача · открой «Мои задачи»";

    public static bool InDesktopList(PlannerTaskDto task, DateOnly today, DateTimeOffset now, bool upcoming) =>
        task.Status == PlannerTaskStatus.Active && (upcoming || task.DueDate <= today ||
            task.ReminderStatus == TaskReminderStatus.Delivered || (task.SnoozedUntilUtc ?? task.RemindAtUtc) <= now);
}
