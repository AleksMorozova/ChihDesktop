using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChihDesktop;

public sealed class ReminderCenter : Window
{
    private readonly IReminderCommands scheduler;
    private readonly TimeProvider clock;
    private readonly Action<ReminderOccurrence> showOccurrence;
    private readonly TabControl tabs = new();
    private readonly StackPanel today = new();
    private readonly StackPanel settings = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly List<EditRow> rows = [];
    private readonly CheckBox quietEnabled = new() { Content = "Тихие часы", Margin = new Thickness(0, 5, 10, 5) };
    private readonly TextBox quietStart = new() { Width = 70, Margin = new Thickness(4) };
    private readonly TextBox quietEnd = new() { Width = 70, Margin = new Thickness(4) };
    private DateOnly displayedDate;

    public ReminderCenter(IReminderCommands scheduler, Action<ReminderOccurrence> showOccurrence, TimeProvider? clock = null)
    {
        this.scheduler = scheduler;
        this.clock = clock ?? TimeProvider.System;
        this.showOccurrence = showOccurrence;
        Title = "Чих — напоминания 🐾";
        Width = 720; Height = 650; MinWidth = 560; MinHeight = 400;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        tabs.Items.Add(new TabItem { Header = "Сегодня", Content = Scroll(today) });
        tabs.Items.Add(new TabItem { Header = "Настройки", Content = Scroll(settings) });
        tabs.Items.Add(new TabItem { Header = "Характер", Content = Scroll(new PersonalitySettingsPanel(scheduler)) });
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(tabs);
        Content = root;
        BuildSettings();
        RefreshToday();
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public void SelectTab(bool settingsTab) { tabs.SelectedIndex = settingsTab ? 1 : 0; RefreshToday(); }
    public void RefreshForDateChange()
    { if (scheduler.LocalDate(clock.GetUtcNow()) != displayedDate) RefreshToday(); }

    public void RefreshToday()
    {
        displayedDate = scheduler.LocalDate(clock.GetUtcNow());
        var data = scheduler.GetSnapshot();
        today.Children.Clear();
        today.Children.Add(Text($"Сегодня: {displayedDate:dd.MM.yyyy} · {data.TimeZoneId}", true));
        today.Children.Add(Text("Мур! «Скрыть» не означает «Готово». Подтверждай выполнение только сам."));
        foreach (var definition in data.Reminders.Where(r => r.Recurrence != ReminderRecurrence.RunningInterval).OrderBy(r => r.Time))
        {
            if (definition.Recurrence == ReminderRecurrence.Weekly && displayedDate.DayOfWeek != definition.WeeklyDay)
            {
                today.Children.Add(Text($"{definition.Title} — {definition.WeeklyDay} в {definition.Time:HH:mm} · {definition.TimeZoneId ?? data.TimeZoneId}"));
                continue;
            }
            var occurrence = data.Occurrences.FirstOrDefault(o => o.ReminderId == definition.Id && o.LocalDate == displayedDate && !o.IsManualTest);
            if (occurrence is null)
                today.Children.Add(Text($"{definition.Time:HH:mm} · {definition.Title} — {(definition.Enabled ? "ожидается" : "выключено")}"));
        }
        var occurrences = data.Occurrences.Where(o => o.LocalDate == displayedDate ||
                o.Acknowledgment == ReminderAcknowledgment.Pending).OrderBy(o => o.DueAtUtc).ToList();
        foreach (var occurrence in occurrences)
        {
            var panel = new StackPanel();
            string state = occurrence.Acknowledgment switch
            {
                ReminderAcknowledgment.Completed => "Готово ✓",
                ReminderAcknowledgment.Dismissed => "Скрыто — выполнение не подтверждено",
                _ => occurrence.SnoozedUntilUtc is { } until
                    ? "Отложено до " + scheduler.LocalNow(until).ToString("dd.MM HH:mm") : "Ожидает ответа"
            };
            panel.Children.Add(Text($"{(occurrence.IsManualTest ? "ТЕСТ · " : "")}{occurrence.LocalDate:dd.MM} {scheduler.LocalNow(occurrence.DueAtUtc):HH:mm} · {occurrence.Title}", true));
            if (occurrence.IsLegacyHistory) panel.Children.Add(Text("Архив прежнего расписания · автоматически не отправляется"));
            panel.Children.Add(Text(state));
            var actions = new WrapPanel();
            if (occurrence.Acknowledgment == ReminderAcknowledgment.Pending)
            {
                AddButton(actions, "Показать", () =>
                {
                    if (scheduler.MayNotify(occurrence, clock.GetUtcNow())) showOccurrence(occurrence);
                    else status.Text = "Сейчас не время этого пункта, тихие часы или запись относится к прежнему расписанию. Она сохранена здесь.";
                });
                if (!occurrence.IsLegacyHistory) AddButton(actions, "Через 10 минут", () => scheduler.Snooze(occurrence.Id, clock.GetUtcNow()));
                AddButton(actions, "Скрыть", () => scheduler.Acknowledge(occurrence.Id, false, clock.GetUtcNow()));
            }
            if (occurrence.RequiresConfirmation && occurrence.Acknowledgment != ReminderAcknowledgment.Completed)
                AddButton(actions, "Готово ✓", () => scheduler.Acknowledge(occurrence.Id, true, clock.GetUtcNow()));
            panel.Children.Add(actions);
            today.Children.Add(new Border { Child = panel, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 5, 8, 3) });
        }
        status.Text = scheduler.PersistenceError ?? scheduler.StartupWarning ??
            (scheduler.IsQuiet(clock.GetUtcNow()) ? "Тихие часы: новые пузырьки подождут. Записи не потеряются." : "Мур! Всё сохранено локально.");
    }

    private void BuildSettings()
    {
        if (quietEnabled.Parent is Panel previousQuiet) previousQuiet.Children.Clear();
        settings.Children.Clear(); rows.Clear();
        var data = scheduler.GetSnapshot();
        settings.Children.Add(Text("Часовой пояс тихих часов: " + data.TimeZoneId + ". Каждое напоминание сохраняет свой пояс с учётом DST.", true));
        settings.Children.Add(Text("Новые напоминания используют пояс из настроек Чиха. Закрытие и скрытие не означают выполнение. Удаление определения сохраняет всю историю."));
        var create = new WrapPanel(); AddButton(create, "Создать напоминание…", () => new ReminderDefinitionWindow(scheduler, () => { BuildSettings(); RefreshToday(); }) { Owner = this }.ShowDialog());
        settings.Children.Add(create);
        var quiet = new WrapPanel();
        quietEnabled.IsChecked = data.QuietHoursEnabled;
        quietStart.Text = data.QuietStart.ToString("HH:mm");
        quietEnd.Text = data.QuietEnd.ToString("HH:mm");
        quiet.Children.Add(quietEnabled); quiet.Children.Add(quietStart); quiet.Children.Add(Text("—")); quiet.Children.Add(quietEnd);
        settings.Children.Add(quiet);
        settings.Children.Add(Text("Формат времени: ЧЧ:ММ. Одинаковые границы — тишина весь день. Исключения включаются отдельно для каждого напоминания."));
        foreach (var definition in data.Reminders)
        {
            var row = new EditRow(definition);
            rows.Add(row);
            settings.Children.Add(row.Panel);
            var remove = new WrapPanel();
            AddButton(remove, "Удалить «" + definition.Title + "»…", () =>
            {
                if (MessageBox.Show(this, "Удалить определение? История, отметки и отложенные записи останутся. Автоматические уведомления этого пункта прекратятся.", "Удаление напоминания", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var result = scheduler.DeleteReminder(definition.Id);
                if (result.Success) { BuildSettings(); RefreshToday(); } else status.Text = result.Error;
            });
            settings.Children.Add(remove);
        }
        var buttons = new WrapPanel();
        AddButton(buttons, "Сохранить", SaveSettings);
        settings.Children.Add(buttons);
        settings.Children.Add(Text("Файл: " + scheduler.ConfigurationPath));
    }

    private void SaveSettings()
    {
        if (!TimeOnly.TryParseExact(quietStart.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(quietEnd.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        { status.Text = "Проверь время тихих часов: например, 22:00 и 08:00."; return; }
        // Validate a detached copy first; a bad row must never partially update live schedules.
        var copies = JsonSerializer.Deserialize<List<ReminderDefinition>>(
            JsonSerializer.Serialize(scheduler.GetSnapshot().Reminders, ReminderStore.JsonOptions), ReminderStore.JsonOptions)!;
        for (int i = 0; i < rows.Count; i++)
            if (!rows[i].Apply(copies[i], out string error)) { status.Text = error; return; }
        var result = scheduler.UpdateSettings(copies, quietEnabled.IsChecked == true, start, end);
        status.Text = result.Success ? "Сохранено. Мур! 🐾" : result.Error;
    }

    private static TextBlock Text(string text, bool bold = false) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 4),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
    private static void AddButton(Panel panel, string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(2, 5, 5, 2), Padding = new Thickness(8, 4, 8, 4) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private sealed class EditRow
    {
        public Border Panel { get; }
        private readonly CheckBox enabled = new();
        private readonly CheckBox bypass = new() { Content = "Показывать даже в тихие часы (моё исключение)", Margin = new Thickness(2, 5, 0, 0) };
        private readonly TextBox title = new() { Margin = new Thickness(2, 3, 2, 3) };
        private readonly TextBox time = new() { Width = 75, Margin = new Thickness(5, 0, 5, 0) };
        private readonly ComboBox weekday = new() { ItemsSource = Enum.GetValues<DayOfWeek>(), Margin = new(5) };
        public EditRow(ReminderDefinition definition)
        {
            enabled.Content = "Включено"; enabled.IsChecked = definition.Enabled;
            bypass.IsChecked = definition.BypassQuietHours;
            title.Text = definition.Title;
            time.Text = definition.Recurrence != ReminderRecurrence.RunningInterval ? definition.Time.ToString("HH:mm") : definition.IntervalMinutes.ToString();
            var content = new StackPanel();
            content.Children.Add(enabled); content.Children.Add(title);
            var schedule = new WrapPanel();
            schedule.Children.Add(Text(definition.Recurrence switch
            {
                ReminderRecurrence.Daily => "Ежедневно в", ReminderRecurrence.Weekly => "Раз в неделю в", _ => "Каждые (минут)"
            }));
            schedule.Children.Add(time);
            weekday.SelectedItem = definition.WeeklyDay;
            if (definition.Recurrence == ReminderRecurrence.Weekly) schedule.Children.Add(weekday);
            content.Children.Add(Text("Пояс: " + (definition.TimeZoneId ?? "исторический пояс хранилища")));
            content.Children.Add(schedule); content.Children.Add(bypass);
            Panel = new Border { Child = content, BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray,
                Padding = new Thickness(10), Margin = new Thickness(0, 5, 8, 5), CornerRadius = new CornerRadius(8) };
        }
        public bool Apply(ReminderDefinition definition, out string error)
        {
            error = "Проверь название и время: " + definition.Title;
            if (string.IsNullOrWhiteSpace(title.Text)) return false;
            if (definition.Recurrence != ReminderRecurrence.RunningInterval)
            {
                if (!TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return false;
                definition.Time = value;
            }
            else
            {
                if (!int.TryParse(time.Text, out int minutes) || minutes is < 1 or > 1440) return false;
                definition.IntervalMinutes = minutes;
            }
            definition.Title = title.Text.Trim(); definition.Enabled = enabled.IsChecked == true;
            definition.WeeklyDay = weekday.SelectedItem is DayOfWeek day ? day : definition.WeeklyDay;
            definition.BypassQuietHours = bypass.IsChecked == true;
            return true;
        }
    }
}

