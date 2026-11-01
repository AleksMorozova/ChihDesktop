using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ChihDesktop;

public sealed class ReminderDefinitionWindow : Window
{
    public ReminderDefinitionWindow(IReminderCommands commands, Action saved)
    {
        AppBranding.Apply(this);
        Title = "Новое напоминание"; Width = 460; Height = 510; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(25) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var title = new TextBox { MaxLength = 200 }; var time = new TextBox { Text = "09:00" };
        var interval = new TextBox { Text = "60" };
        var recurrence = new ComboBox { ItemsSource = new[] { new Choice<ReminderRecurrence>(ReminderRecurrence.Daily, "Ежедневно"), new(ReminderRecurrence.Weekly, "Раз в неделю"), new(ReminderRecurrence.RunningInterval, "Пока приложение работает") }, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = ReminderRecurrence.Daily };
        var day = new ComboBox { ItemsSource = Enum.GetValues<DayOfWeek>().Select(d => new Choice<DayOfWeek>(d, CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.GetDayName(d))).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = DayOfWeek.Monday };
        var enabled = new CheckBox { Content = "Включить это напоминание", Margin = new(0, 12, 0, 10), IsChecked = false };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Field(string name, Control input) { panel.Children.Add(new Label { Content = name, Target = input }); input.Padding = new(6); panel.Children.Add(input); }
        Field("Название", title); Field("Повтор", recurrence); Field("Время (ЧЧ:ММ)", time); Field("День недели", day); Field("Интервал минут (для режима работы приложения)", interval);
        panel.Children.Add(enabled);
        var save = new Button { Content = "Создать", Padding = new(12, 8, 12, 8) }; panel.Children.Add(save); panel.Children.Add(status);
        save.Click += (_, _) =>
        {
            var repeat = (ReminderRecurrence)recurrence.SelectedValue;
            if (string.IsNullOrWhiteSpace(title.Text) || !TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due) || !int.TryParse(interval.Text, out int minutes) || minutes is < 1 or > 1440)
            { status.Text = "Проверь название, время и интервал от 1 до 1440 минут."; return; }
            var result = commands.CreateReminder(new ReminderDefinition { Title = title.Text.Trim(), Time = due, Recurrence = repeat, IntervalMinutes = minutes, WeeklyDay = (DayOfWeek)day.SelectedValue, Enabled = enabled.IsChecked == true });
            if (result.Success) { saved(); Close(); } else status.Text = result.Error;
        };
    }
    private sealed record Choice<T>(T Value, string Label);
}
