using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChihDesktop;

public sealed class PersonalitySettingsPanel : StackPanel
{
    private readonly CheckBox enabled = new() { Content = "Характер Чиха включён" };
    private readonly CheckBox spontaneous = new() { Content = "Редкие спонтанные комментарии" };
    private readonly CheckBox quiet = new() { Content = "Дополнительные тихие часы для болтовни" };
    private readonly TextBox interval = new() { Width = 70 };
    private readonly TextBox start = new() { Width = 70 };
    private readonly TextBox end = new() { Width = 70 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 4) };

    public PersonalitySettingsPanel(IReminderCommands scheduler)
    {
        Margin = new Thickness(10);
        var config = scheduler.GetPersonalityOptions();
        enabled.IsChecked = config.PersonalityEnabled;
        spontaneous.IsChecked = config.SpontaneousCommentsEnabled;
        quiet.IsChecked = config.QuietHoursEnabled;
        interval.Text = config.MinimumCommentIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        start.Text = config.QuietStart.ToString("HH:mm");
        end.Text = config.QuietEnd.ToString("HH:mm");
        AddText("Чих — управляющий домом и Senior .NET Developer", true);
        AddText("Самодовольный, ласковый и слегка ленивый. Настроение меняется понемногу; спрайты и движение остаются прежними.");
        Children.Add(enabled);
        Children.Add(spontaneous);
        var rate = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        rate.Children.Add(new TextBlock { Text = "Минимум минут между комментариями: ", VerticalAlignment = VerticalAlignment.Center });
        rate.Children.Add(interval); Children.Add(rate);
        Children.Add(quiet);
        var times = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
        times.Children.Add(start);
        times.Children.Add(new TextBlock { Text = " — ", VerticalAlignment = VerticalAlignment.Center });
        times.Children.Add(end); Children.Add(times);
        AddText("Болтовня использует пояс хранилища напоминаний: " + scheduler.GetSnapshot().TimeZoneId + ". Одинаковые границы — тишина весь день.");
        AddText("По умолчанию: не чаще 20 минут, плюс случайная пауза до 5 минут. Во время полноэкранного приложения Чих молчит. Поглаживания вызывают ответ лишь иногда и с паузой между ответами.");
        AddText("Напоминания работают независимо от характера. «Скрыть» не значит «Готово»; лекарства никогда не отмечаются автоматически.");
        var save = new Button { Content = "Сохранить характер", HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 12, 0, 0) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(interval.Text, out int minutes) || minutes is < 1 or > 1440 ||
                !TimeOnly.TryParseExact(start.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
                !TimeOnly.TryParseExact(end.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
            { status.Text = "Интервал: 1–1440 минут. Время: ЧЧ:ММ, например 22:00."; return; }
            var result = scheduler.UpdatePersonality(new PersonalityOptions
            {
                PersonalityEnabled = enabled.IsChecked == true, SpontaneousCommentsEnabled = spontaneous.IsChecked == true,
                MinimumCommentIntervalMinutes = minutes, QuietHoursEnabled = quiet.IsChecked == true, QuietStart = from, QuietEnd = until
            });
            status.Text = result.Success ? "Сохранено. Начальство довольно 🐾" : result.Error;
        };
        Children.Add(save); Children.Add(status);
    }

    private void AddText(string text, bool bold = false) => Children.Add(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brushes.DarkSlateGray
    });
}
