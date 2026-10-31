using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class R1OnboardingTests
{
    [Fact]
    public void Native_welcome_constructs_without_WebView_and_closing_does_not_complete_setup() => OnSta(() =>
    {
        using var files = new TempFiles(); var settings = new UserSettingsService(new MemorySettings(), new MemoryAutostart());
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        var window = new UserSettingsWindow(settings, commands, true);
        Assert.Contains(Descendants(window).OfType<TextBlock>(), t => t.Text == "Привет. Я Чих.");
        Assert.Single(Descendants(window).OfType<Image>());
        window.Close(); Assert.True(settings.ShouldOnboard); Assert.Empty(commands.GetSnapshot().Reminders);
    });

    [Fact]
    public void Finishing_without_opt_in_does_not_create_or_enable_any_reminder() => OnSta(() =>
    {
        using var files = new TempFiles(); var registry = new MemoryAutostart(); var settings = new UserSettingsService(new MemorySettings(), registry);
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        var window = new UserSettingsWindow(settings, commands, true);
        for (int i = 0; i < 3; i++) Click(window, "Далее");
        Assert.False(Descendants(window).OfType<CheckBox>().Single(c => c.Content?.ToString() == "Создать и включить моё первое напоминание").IsChecked);
        Click(window, "Готово");
        Assert.False(settings.ShouldOnboard); Assert.Empty(commands.GetSnapshot().Reminders); Assert.Equal(0, registry.Writes);
    });

    [Fact]
    public void Failed_completion_after_opt_in_is_retryable_without_duplicate_reminder() => OnSta(() =>
    {
        using var files = new TempFiles(); var store = new SequencedSettings { FailAt = 2 };
        var settings = new UserSettingsService(store, new MemoryAutostart());
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        var window = new UserSettingsWindow(settings, commands, true);
        for (int i = 0; i < 3; i++) Click(window, "Далее");
        Descendants(window).OfType<CheckBox>().Single(c => c.Content?.ToString() == "Создать и включить моё первое напоминание").IsChecked = true;
        Descendants(window).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Название — любое твоё дело").Text = "Synthetic opt-in reminder";
        Click(window, "Готово");
        Assert.True(settings.ShouldOnboard); Assert.Single(commands.GetSnapshot().Reminders);
        Assert.True(commands.GetSnapshot().Reminders.Single().Enabled);
        Assert.Contains(Descendants(window).OfType<TextBlock>(), t => t.Text.Contains("завершение пока не записано"));
        store.FailAt = 0; Click(window, "Готово");
        Assert.False(settings.ShouldOnboard); Assert.Single(commands.GetSnapshot().Reminders);
    });

    [Fact]
    public void Explicit_skip_is_persisted_without_optional_input_or_reminder_creation() => OnSta(() =>
    {
        using var files = new TempFiles(); var settings = new UserSettingsService(new MemorySettings(), new MemoryAutostart());
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        var window = new UserSettingsWindow(settings, commands, true);
        Click(window, "Пропустить знакомство");
        Assert.True(settings.GetSnapshot().OnboardingCompleted); Assert.Equal("", settings.GetSnapshot().OwnerDisplayName);
        Assert.Empty(commands.GetSnapshot().Reminders); Assert.False(File.Exists(files.File("reminders.json")));
    });

    private static void Click(Window window, string caption) => Descendants(window).OfType<Button>().Single(b => b.Content?.ToString() == caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Descendants(child)) yield return item;
    }
    private static void OnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Native onboarding test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class SequencedSettings : IUserSettingsPersistence
    {
        private UserSettings data = new(); private int saves;
        public int FailAt;
        public string? Error => "Synthetic final commit failure";
        public SettingsLoadResult Load() => new(data, true, false, null);
        public bool Save(UserSettings settings) { if (++saves == FailAt) return false; data = ReminderDataCodec.Copy(settings); return true; }
    }
}
