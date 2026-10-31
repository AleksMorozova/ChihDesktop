using ChihDesktop;
using System.Text.Json;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class PersonalityRegressionTests
{
    [Fact]
    public void Personality_workflow_preserves_all_35_assertions()
    {
        int checks = 0;
        void Check(bool condition, string label)
        { Assert.True(condition, label); checks++; Console.WriteLine("PASS " + label); }
        var now = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.FromHours(2));
        var config = new PersonalityOptions();
        PersonalityService Create() => new(() => config, now, new Random(13));
        var service = Create();
        Check(DialogueCatalog.Phrases.Count == 9 && DialogueCatalog.Phrases.Values.Sum(p => p.Length) >= 35, "all nine categories and at least 35 phrases");
        Check(DialogueCatalog.Phrases.Values.SelectMany(p => p).Distinct().Count() == DialogueCatalog.Phrases.Values.Sum(p => p.Length), "all catalog phrases distinct");
        foreach (var category in Enum.GetValues<DialogueCategory>())
        {
            string? previous = null;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var phrases = new HashSet<string>();
                for (int i = 0; i < DialogueCatalog.Phrases[category].Length; i++)
                {
                    string phrase = service.NextPhrase(category);
                    if (previous == phrase || !phrases.Add(phrase)) throw new Exception("Repeated phrase: " + category);
                    previous = phrase;
                }
            }
            Check(true, category + " shuffle cycles exhaust before repeating, no boundary repeat");
        }
        Check(service.IsQuiet(now.Date.AddHours(23), false) && service.IsQuiet(now.Date.AddHours(7), false) &&
            !service.IsQuiet(now.Date.AddHours(12), false), "personality overnight quiet hours");
        config.QuietHoursEnabled = false;
        Check(service.IsQuiet(now.Date.AddHours(12), true), "shared reminder quiet hours cannot be bypassed by chatter");
        config.QuietHoursEnabled = true;
        Check(service.Spontaneous(now, true) is null, "no spontaneous comment at startup");
        Check(service.Spontaneous(now.AddMinutes(30), false) is null, "blocked/fullscreen/quiet context does not speak");
        Check(service.Spontaneous(now.AddMinutes(30), true) is not null, "infrequent spontaneous comment when allowed");
        Check(service.Spontaneous(now.AddMinutes(31), true) is null, "minimum spontaneous interval enforced");
        service.RecordSpeech(now.AddMinutes(31));
        Check(service.Spontaneous(now.AddMinutes(50), true) is null, "reminder speech also suppresses immediate chatter");
        config.SpontaneousCommentsEnabled = false;
        Check(service.Spontaneous(now.AddHours(3), true) is null, "spontaneous opt-out");
        Check(service.RequestedPhrase(now.AddHours(3), true, true) is not null, "explicit phrase still works with spontaneous disabled");
        config.PersonalityEnabled = false;
        Check(service.RequestedPhrase(now.AddHours(4), true, true) is null && service.PetReply(now.AddHours(4), true) is null,
            "master personality off suppresses manual and petting dialogue");
        config.PersonalityEnabled = config.SpontaneousCommentsEnabled = true;
        service = Create();
        service.Pet(now);
        Check(service.PetReply(now, false) is null, "petting never overrides unavailable bubble or dragging gate");
        int replies = 0;
        for (int i = 0; i < 100; i++) { service.Pet(now.AddMilliseconds(i * 10)); if (service.PetReply(now.AddMilliseconds(i * 10), true) is not null) replies++; }
        Check(replies == 1, "rapid clicking produces at most one response with chance gate");

        service = Create();
        service.Pet(now.AddMinutes(2));
        service.Update(now.AddMinutes(2), now.DateTime.AddMinutes(2), TimeSpan.FromSeconds(5), TimeSpan.Zero, CatState.Idle);
        Check(service.Mood == ChihMood.Content, "mood change has cooldown");
        service.Update(now.AddMinutes(3), now.DateTime.AddMinutes(3), TimeSpan.FromSeconds(5), TimeSpan.Zero, CatState.Idle);
        Check(service.Mood == ChihMood.Playful, "recent petting gradually allows playful mood");
        service = Create();
        for (int i = 1; i <= 180; i++) service.Update(now.AddSeconds(i * 5), now.Date.AddHours(23).AddSeconds(i * 5), TimeSpan.FromSeconds(5), TimeSpan.Zero, CatState.Sleeping);
        Check(service.Mood == ChihMood.Sleepy, "night and sleep gradually lead to sleepy mood");
        service = Create();
        for (int i = 1; i <= 600; i++) service.Update(now.AddSeconds(i * 5), now.DateTime.AddSeconds(i * 5), TimeSpan.FromSeconds(5), TimeSpan.Zero, CatState.Idle);
        Check(service.Mood == ChihMood.Demanding, "long absence of petting leads to attention demand");
        service = Create();
        for (int i = 1; i <= 48; i++) service.Update(now.AddSeconds(i * 5), now.DateTime.AddSeconds(i * 5), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10), CatState.Idle);
        Check(service.Mood == ChihMood.Curious, "inactivity permits curious mood");
        service.Update(now.AddHours(8), now.DateTime.AddHours(8), TimeSpan.FromHours(8), TimeSpan.Zero, CatState.Sleeping);
        Check(service.Spontaneous(now.AddHours(8), true) is null, "resume defers chatter rather than replaying it");

        var reminder = new ReminderOccurrence { ReminderId = "sample-a", Title = "Пример A + упражнения",
            Message = "Проверь напоминание.", RequiresConfirmation = true };
        string original = JsonSerializer.Serialize(reminder);
        string text = service.ReminderMessage(reminder, true);
        Check(text.Contains(reminder.Message) && text.Contains("упражнен", StringComparison.OrdinalIgnoreCase), "medication and exercise remain explicit");
        Check(JsonSerializer.Serialize(reminder) == original && reminder.Acknowledgment == ReminderAcknowledgment.Pending,
            "humor never changes acknowledgment or reminder data");
        config.PersonalityEnabled = false;
        Check(service.ReminderMessage(reminder, true) == reminder.Message, "off preserves plain reminders");
        config.PersonalityEnabled = true;
        Check(service.ReminderMessage(reminder, false) == reminder.Message, "quiet override reminder may display without personality");
        var path = Path.GetFullPath(Path.Combine("tools", "PersonalityChecks", "TestOutput", Guid.NewGuid().ToString("N"), "reminders.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(SyntheticReminderFixtures.Schedule(), ReminderStore.JsonOptions));
        var scheduler = new ReminderScheduler(new ReminderStore(path));
        string legacy = File.ReadAllText(path);
        using (var document = JsonDocument.Parse(legacy))
        {
            var oldData = document.RootElement.EnumerateObject().Where(p => p.Name != "Personality")
                .ToDictionary(p => p.Name, p => p.Value.Clone());
            File.WriteAllText(path, JsonSerializer.Serialize(oldData));
        }
        scheduler = new ReminderScheduler(new ReminderStore(path));
        Check(scheduler.Data.Reminders.Count == 7 && scheduler.Data.Personality.PersonalityEnabled, "old reminder JSON migrates without replacing reminders");
        scheduler.Data.Personality.PersonalityEnabled = false;
        scheduler.Data.Personality.MinimumCommentIntervalMinutes = 45;
        scheduler.SavePreferences();
        scheduler = new ReminderScheduler(new ReminderStore(path));
        Check(!scheduler.Data.Personality.PersonalityEnabled && scheduler.Data.Personality.MinimumCommentIntervalMinutes == 45,
            "personality preferences persist");
        Console.WriteLine($"{checks} personality assertions passed; catalog contains {DialogueCatalog.Phrases.Values.Sum(p => p.Length)} phrases.");
        Assert.Equal(35, checks);
    }

}

