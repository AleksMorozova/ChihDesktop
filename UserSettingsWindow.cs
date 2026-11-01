using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChihDesktop;

// Modeless native setup also works without WebView2, internet or a development runtime.
public sealed class UserSettingsWindow : Window
{
    private readonly UserSettingsService service;
    private readonly IReminderCommands reminders;
    private readonly StackPanel body = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed, Margin = new(0, 12, 0, 8) };
    private readonly TextBlock heading = new() { FontSize = 27, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 8) };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBox name = new() { MaxLength = 60 };
    private readonly ComboBox zone = new();
    private readonly CheckBox login = new() { Content = "Запускать при входе в Windows (только для меня)" };
    private readonly CheckBox petOnly = new() { Content = "Начинать только с кота, без окна планировщика" };
    private readonly CheckBox topmost = new() { Content = "Держать Чиха поверх обычных окон" };
    private readonly CheckBox personality = new() { Content = "Включить характер Чиха" };
    private readonly CheckBox spontaneous = new() { Content = "Редкие самостоятельные комментарии" };
    private readonly CheckBox notifications = new() { Content = "Показывать уведомления напоминаний" };
    private readonly CheckBox taskNotifications = new() { Content = "Уведомления задач (только настроенные вручную)" };
    private readonly CheckBox taskSummary = new() { Content = "Редкие сводки задач" };
    private readonly CheckBox taskTitles = new() { Content = "Показывать названия задач в пузырях" };
    private readonly CheckBox taskReactions = new() { Content = "Реакции Чиха на выполнение задач" };
    private readonly TextBox interval = new();
    private readonly CheckBox firstEnabled = new() { Content = "Создать и включить моё первое напоминание" };
    private readonly TextBox firstTitle = new() { MaxLength = 200 };
    private readonly TextBox firstTime = new() { Text = "09:00" };
    private int step;
    private bool onboarding, reminderCreated;
    private readonly string firstId = Guid.NewGuid().ToString("N");

    public UserSettingsWindow(UserSettingsService service, IReminderCommands reminders, bool onboarding)
    {
        this.service = service; this.reminders = reminders; this.onboarding = onboarding;
        var current = service.GetSnapshot();
        AppBranding.Apply(this);
        Title = "ChihDesktop — настройки"; Width = 650; Height = 740; MinWidth = 530; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 245));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel { Margin = new(30) };
        var header = new StackPanel(); header.Children.Add(Text("CHIH · ЛИЧНОЕ ПРОСТРАНСТВО", true)); header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var bottom = new StackPanel(); bottom.Children.Add(status); bottom.Children.Add(actions);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        name.Text = current.OwnerDisplayName;
        var zones = TimeZoneInfo.GetSystemTimeZones().Select(z => z.Id).Append(current.TimeZoneId).Distinct().Order().ToArray();
        zone.DisplayMemberPath = nameof(ZoneOption.Label); zone.SelectedValuePath = nameof(ZoneOption.Id);
        zone.ItemsSource = zones.Select(id => new ZoneOption(id, AppTimeZones.Get(id).DisplayName + " · " + id)).ToArray();
        zone.SelectedValue = current.TimeZoneId;
        login.IsChecked = current.LaunchAtSignIn; petOnly.IsChecked = current.StartPetOnly; topmost.IsChecked = current.PetTopmost;
        personality.IsChecked = current.Personality.PersonalityEnabled; spontaneous.IsChecked = current.Personality.SpontaneousCommentsEnabled;
        interval.Text = current.Personality.MinimumCommentIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        notifications.IsChecked = current.ReminderNotificationsEnabled;
        taskNotifications.IsChecked = current.TaskReminderNotificationsEnabled; taskSummary.IsChecked = current.TaskSummarySpeechEnabled; taskTitles.IsChecked = current.ShowTaskTitlesInBubbles; taskReactions.IsChecked = current.TaskCompletionReactionsEnabled;
        step = onboarding ? 0 : 1;
        Render();
        status.Text = service.Error ?? reminders.PersistenceError ?? "";
    }
    private static TextBlock Text(string text, bool bold = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 10), Foreground = new SolidColorBrush(Color.FromRgb(53, 75, 59)), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    private void Field(string label, Control control)
    {
        AutomationProperties.SetName(control, label);
        body.Children.Add(new Label { Content = label, Target = control, Padding = new(0), Margin = new(0, 10, 0, 5) });
        control.Padding = new(8); control.Margin = new(0, 0, 0, 10); body.Children.Add(control);
    }
    private void AddButton(string label, Action action, bool enabled = true)
    {
        var button = new Button { Content = label, Margin = new(8, 8, 0, 0), Padding = new(15, 9, 15, 9), IsEnabled = enabled };
        button.Click += (_, _) => action(); actions.Children.Add(button);
    }
    private void Render()
    {
        body.Children.Clear(); actions.Children.Clear(); status.Text = service.Error ?? reminders.PersistenceError ?? "";
        heading.Text = !onboarding ? "Настройки Чиха" : new[] { "Привет. Я Чих.", "Как к тебе обращаться?", "У каждого кота свой режим", "Первое напоминание — по желанию" }[step];
        if (onboarding) body.Children.Add(Text($"Шаг {step + 1} из 4 · можно пропустить необязательное"));
        if (onboarding && step == 0)
        {
            body.Children.Add(new Image { Width = 160, Height = 130, Source = new BitmapImage(new Uri("pack://application:,,,/ChihDesktop;component/Assets/Sprites/Idle-00.png")) });
            body.Children.Add(Text("Я умею гулять по рабочему столу, напоминать о твоих планах и немного руководить. Большая часть руководства происходит лёжа.", true));
            body.Children.Add(Text("Всё хранится на этом компьютере. Аккаунты и интернет не нужны. Знакомство не сбрасывает существующие записи. Новые задачи и напоминания добавляешь только ты."));
        }
        if (!onboarding || step == 1)
        {
            Field("Имя для обращения — необязательно", name); Field("Предпочтительный часовой пояс", zone);
            body.Children.Add(Text("Русский интерфейс. Выбранный пояс применяется к новым напоминаниям и датам планировщика. Уже созданные напоминания сохраняют свой пояс и местное время. История и отложенные UTC-моменты не пересчитываются."));
        }
        if (!onboarding || step == 2)
        {
            foreach (var box in new[] { petOnly, topmost, personality, spontaneous, notifications, login, taskNotifications, taskSummary, taskTitles, taskReactions })
            { box.Margin = new(0, 9, 0, 9); body.Children.Add(box); }
            Field("Минимум минут между комментариями (1–1440)", interval);
            body.Children.Add(Text("Отключение уведомлений сохраняет ожидающие записи. Тихие часы настраиваются в окне напоминаний. Автозапуск по умолчанию выключен и не требует прав администратора."));
        }
        if (onboarding && step == 3)
        {
            firstEnabled.IsEnabled = reminders.PersistenceError is null && (reminders.GetSnapshot().Reminders.Count == 0 || reminderCreated);
            body.Children.Add(firstEnabled); Field("Название — любое твоё дело", firstTitle); Field("Ежедневно в (ЧЧ:ММ)", firstTime);
            body.Children.Add(Text("Можно ничего не добавлять. Напоминание включится только по твоему выбору. Любые другие напоминания создаются из меню кота. Никаких медицинских данных вводить не нужно."));
        }
        if (onboarding)
        {
            AddButton("Пропустить знакомство", () => { var result = service.CompleteOnboarding(true); if (result.Success) Close(); else status.Text = result.Error; }, service.CanWrite);
            if (step > 0) AddButton("Назад", () => { step--; Render(); });
            AddButton(step < 3 ? "Далее" : "Готово", () => { if (step < 3) { step++; Render(); } else Save(true); }, service.CanWrite);
        }
        else
        {
            AddButton("Знакомство заново", () => { onboarding = true; step = 0; Render(); });
            AddButton("Сохранить", () => Save(false), service.CanWrite);
        }
    }
    private void Save(bool finish)
    {
        if (!int.TryParse(interval.Text, out int minutes) || minutes is < 1 or > 1440) { status.Text = "Интервал — от 1 до 1440 минут."; return; }
        TimeOnly firstDue = default;
        bool create = finish && firstEnabled.IsEnabled && firstEnabled.IsChecked == true && !reminderCreated;
        if (create && (string.IsNullOrWhiteSpace(firstTitle.Text) || !TimeOnly.TryParseExact(firstTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out firstDue)))
        { status.Text = "Проверь название и время или отключи создание напоминания."; return; }
        var previous = service.GetSnapshot(); var options = ReminderDataCodec.Copy(previous.Personality);
        options.PersonalityEnabled = personality.IsChecked == true; options.SpontaneousCommentsEnabled = spontaneous.IsChecked == true; options.MinimumCommentIntervalMinutes = minutes;
        var result = service.Save(previous with { OwnerDisplayName = name.Text, TimeZoneId = zone.SelectedValue as string ?? previous.TimeZoneId,
            LaunchAtSignIn = login.IsChecked == true, StartPetOnly = petOnly.IsChecked == true, PetTopmost = topmost.IsChecked == true,
            ReminderNotificationsEnabled = notifications.IsChecked == true, TaskReminderNotificationsEnabled = taskNotifications.IsChecked == true, TaskSummarySpeechEnabled = taskSummary.IsChecked == true, ShowTaskTitlesInBubbles = taskTitles.IsChecked == true, TaskCompletionReactionsEnabled = taskReactions.IsChecked == true, Personality = options });
        if (!result.Success) { status.Text = result.Error; return; }
        if (create)
        {
            var reminder = reminders.CreateReminder(new ReminderDefinition { Id = firstId, Title = firstTitle.Text.Trim(), Time = firstDue, Enabled = true });
            if (!reminder.Success) { status.Text = "Общие настройки сохранены, но напоминание не создано. " + reminder.Error; return; }
            reminderCreated = true;
        }
        if (finish)
        {
            result = service.CompleteOnboarding(false);
            if (!result.Success) { status.Text = "Настройки сохранены; завершение пока не записано. " + result.Error; return; }
        }
        Close();
    }
    private sealed record ZoneOption(string Id, string Label);
}

public sealed class UserSettingsWindowManager(UserSettingsService service, IReminderCommands reminders) : IDisposable
{
    private UserSettingsWindow? window;
    public void Show(bool onboarding = false)
    {
        if (window is null) { window = new(service, reminders, onboarding); window.Closed += (_, _) => window = null; }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        new WindowPositionService(window).FitToWorkArea(); window.Activate();
    }
    public void Dispose() { window?.Close(); window = null; }
}
