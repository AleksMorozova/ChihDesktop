using ChihDesktop;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class ReminderRegressionTests
{
    [Fact]
    public void Scheduling_workflow_preserves_all_65_assertions()
    {
        string folder = Path.GetFullPath(Path.Combine("tools", "ReminderChecks", "TestOutput", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        int assertions = 0;
        void Check(bool condition, string name)
        { Assert.True(condition, name); assertions++; Console.WriteLine("PASS " + name); }
        ReminderScheduler Create(string name)
        {
            string path = Path.Combine(folder, name + ".json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(SyntheticReminderFixtures.Schedule(), ReminderStore.JsonOptions));
            return new(new ReminderStore(path));
        }
        DateTimeOffset Utc(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var zero = TimeSpan.Zero;

        var daily = Create("daily");
        Check(daily.Data.Reminders.Count == 7 && daily.Data.Reminders.All(r => r.Enabled), "seven enabled definitions, including separate break mode");
        Check(daily.Data.Reminders.Where(r => r.Recurrence == ReminderRecurrence.Daily).Select(r => r.Time.ToString("HH:mm"))
            .SequenceEqual(new[] { "12:30", "15:00", "15:00", "21:00", "21:00" }), "new exact daily times");
        var before = Utc("2026-10-31T10:29:00Z"); // 11:29 Kyiv, UTC+2
        daily.Tick(before, zero);
        Check(daily.Data.Occurrences.Count == 0, "not early");
        daily.Tick(before.AddMinutes(1), zero);
        Check(daily.Data.Occurrences.Count == 1 && daily.Data.Reminders[1].LastFiredLocalDate == new DateOnly(2026, 10, 31), "daily due and local date persisted");
        daily.Tick(before.AddMinutes(1), zero);
        Check(daily.Data.Occurrences.Count == 1, "same tick cannot duplicate");
        daily = new ReminderScheduler(new ReminderStore(daily.ConfigurationPath));
        daily.Tick(before.AddMinutes(2), zero);
        Check(daily.Data.Occurrences.Count == 1, "restart cannot duplicate");
        var first = daily.TakeNext(before.AddMinutes(2))!;
        Check(first is not null && first.Acknowledgment == ReminderAcknowledgment.Pending, "presentation never completes medication");
        daily.Snooze(first!, before.AddMinutes(2));
        Check(!daily.MayNotify(first!, before.AddMinutes(11)), "snooze not early");
        Check(daily.TakeNext(before.AddMinutes(12))?.Id == first!.Id, "snooze due at ten minutes");
        daily.Acknowledge(first!, false, before.AddMinutes(12));
        Check(first!.Acknowledgment == ReminderAcknowledgment.Dismissed && first.Acknowledgment != ReminderAcknowledgment.Completed, "dismiss is not taken");
        daily.Acknowledge(first, true, before.AddMinutes(13));
        Check(first.Acknowledgment == ReminderAcknowledgment.Completed, "explicit completion");

        var resume = Create("resume");
        resume.Tick(Utc("2026-10-31T20:00:00Z"), TimeSpan.FromHours(10));
        Check(resume.Data.Occurrences.Count == 5, "resume catches up today's five daily items without interval backlog");
        Check(resume.LocalNow(resume.Data.Occurrences[0].DueAtUtc).TimeOfDay == new TimeSpan(12, 30, 0), "catch-up retains scheduled local due time");
        Check(resume.TakeNext(Utc("2026-10-31T20:00:00Z")) is not null &&
            resume.TakeNext(Utc("2026-10-31T20:00:00Z")) is null, "resume one bubble, two-minute queue spacing");
        resume.Tick(Utc("2026-11-01T20:00:00Z"), TimeSpan.FromDays(1));
        Check(resume.Data.Occurrences.Count == 10, "only current local day is created, prior pending retained");
        resume.Tick(Utc("2026-10-31T20:00:00Z"), zero);
        Check(resume.Data.Occurrences.Count == 10, "backward clock does not replay day");

        var quiet = Create("quiet");
        var night = Utc("2026-10-31T21:30:00Z");
        Check(quiet.IsQuiet(night) && quiet.IsQuiet(Utc("2026-11-01T06:59:00Z")) &&
            !quiet.IsQuiet(Utc("2026-11-01T07:00:00Z")), "overnight quiet boundaries");
        var test = quiet.AddTest(night);
        Check(quiet.MayNotify(test, night), "explicit labeled preview works immediately during quiet hours");
        quiet.Tick(night, zero);
        Check(!quiet.MayNotify(quiet.Data.Occurrences.First(o => !o.IsManualTest), night), "real medication still respects quiet hours");
        quiet.Data.Reminders[0].BypassQuietHours = true;
        quiet.SettingsChanged();
        Check(quiet.TakeNext(night)?.Id == test.Id, "explicit per-reminder quiet override");
        Check(quiet.AddTest(night).Id != test.Id, "each explicit preview has isolated test state");

        var interval = Create("interval");
        var morning = Utc("2026-10-31T08:00:00Z");
        for (int i = 1; i <= 24; i++) interval.Tick(morning.AddSeconds(i * 5), TimeSpan.FromSeconds(5));
        Check(interval.Data.Occurrences.Count == 1, "two-minute active interval");
        interval.Tick(morning.AddHours(8), TimeSpan.FromHours(8));
        Check(interval.Data.Occurrences.Count(o => o.ReminderId == "test-break") == 1, "suspend does not replay break intervals");
        for (int i = 1; i <= 24; i++) interval.Tick(morning.AddHours(8).AddSeconds(i * 5), TimeSpan.FromSeconds(5));
        Check(interval.Data.Occurrences.Count(o => o.ReminderId == "test-break") == 1, "pending interval coalesces");
        interval.Data.Reminders[0].Enabled = false;
        Check(!interval.MayNotify(interval.Data.Occurrences[0], morning), "disabled reminder suppressed");

        var dst = Create("dst");
        dst.Data.Reminders = [new ReminderDefinition { Id = "dst", Time = new TimeOnly(3, 30) }];
        dst.Tick(Utc("2026-10-25T00:30:00Z"), zero);
        dst.Tick(Utc("2026-10-25T01:30:00Z"), zero);
        Check(dst.Data.Occurrences.Count == 1, "repeated DST hour fires only once");
        dst = Create("spring");
        dst.Data.Reminders = [new ReminderDefinition { Id = "spring", Time = new TimeOnly(3, 30) }];
        dst.Tick(Utc("2026-03-29T00:59:00Z"), zero);
        Check(dst.Data.Occurrences.Count == 0, "spring reminder not early");
        dst.Tick(Utc("2026-03-29T01:00:00Z"), zero);
        Check(dst.Data.Occurrences.Count == 1, "nonexistent spring hour catches up after jump");

        var persistence = new ReminderScheduler(new ReminderStore(quiet.ConfigurationPath));
        Check(persistence.Data.Reminders[0].BypassQuietHours && persistence.Data.Occurrences[0].NotifiedAtUtc is not null,
            "configuration and delivery state survive restart");
        var corruptPath = Path.Combine(folder, "corrupt.json");
        File.WriteAllText(corruptPath, "{invalid");
        var corrupt = new ReminderScheduler(new ReminderStore(corruptPath));
        Check(corrupt.StartupWarning is not null && Directory.GetFiles(folder, "corrupt.json.invalid-*").Length == 1,
            "corrupt input preserved with warning");
        var afternoon = Create("afternoon");
        afternoon.Data.Reminders.First(r => r.Id == "sample-a").Enabled = false;
        var atTwo = Utc("2026-10-31T13:00:00Z");
        afternoon.Tick(atTwo.AddSeconds(-1), zero);
        Check(afternoon.Data.Occurrences.Count == 0, "14:00 pair is not early");
        afternoon.Tick(atTwo, zero);
        var pair = afternoon.TakeNextGroup(atTwo)!;
        Check(pair.Count == 2 && pair.Select(o => o.ReminderId).ToHashSet().SetEquals(["sample-b", "lunch"]), "14:00 produces one medication-plus-meal group");
        Check(pair.All(o => o.NotifiedAtUtc == atTwo && o.Acknowledgment == ReminderAcknowledgment.Pending), "both group delivery states persist without completion");
        var medication = pair.First(o => o.ReminderId == "sample-b");
        var meal = pair.First(o => o.ReminderId == "lunch");
        afternoon.Acknowledge(medication, true, atTwo);
        Check(meal.Acknowledgment == ReminderAcknowledgment.Pending, "medication confirmation does not confirm meal");
        afternoon.Snooze(meal, atTwo);
        afternoon = new ReminderScheduler(new ReminderStore(afternoon.ConfigurationPath));
        Check(afternoon.TakeNextGroup(atTwo.AddMinutes(9)) is null, "per-item snooze survives restart and is not early");
        var snoozed = afternoon.TakeNextGroup(atTwo.AddMinutes(10))!;
        Check(snoozed.Count == 1 && snoozed[0].ReminderId == "lunch", "snoozed meal does not replay completed medication");
        afternoon.Acknowledge(snoozed[0], false, atTwo.AddMinutes(10));
        Check(snoozed[0].Acknowledgment == ReminderAcknowledgment.Dismissed &&
            afternoon.Data.Occurrences.First(o => o.ReminderId == "sample-b").Acknowledgment == ReminderAcknowledgment.Completed,
            "meal dismissal keeps separate medication completion");

        var partial = Create("partial");
        partial.Data.Reminders.First(r => r.Id == "sample-a").Enabled = false;
        partial.Tick(atTwo, zero);
        var partialPair = partial.TakeNextGroup(atTwo)!;
        partial.Snooze(partialPair[0], atTwo);
        var returned = partial.TakeNextGroup(atTwo.AddMinutes(10))!;
        Check(returned.Count == 1 && returned[0].Id == partialPair[0].Id,
            "snooze does not replay already-shown but unacknowledged sibling");
        partial = new ReminderScheduler(new ReminderStore(partial.ConfigurationPath));
        Check(partial.TakeNextGroup(atTwo.AddMinutes(11)) is null &&
            partial.Data.Occurrences.All(o => o.Acknowledgment == ReminderAcknowledgment.Pending),
            "closing or restart leaves pending items without duplicate delivery");

        var evening = Create("evening");
        foreach (var definition in evening.Data.Reminders) definition.Enabled = definition.BubbleGroup == "evening";
        var atEight = Utc("2026-10-31T19:00:00Z");
        evening.Tick(atEight, zero);
        Check(evening.TakeNextGroup(atEight)!.Select(o => o.ReminderId).ToHashSet().SetEquals(["sample-c", "dinner"]),
            "fictional evening pair groups independent items");

        var priority = Create("priority");
        priority.Data.Reminders.First(r => r.Id == "sample-a").Enabled = false;
        for (int i = 1; i <= 24; i++) priority.Tick(atTwo.AddMinutes(-3).AddSeconds(i * 5), TimeSpan.FromSeconds(5));
        priority.Tick(atTwo, zero);
        Check(priority.TakeNextGroup(atTwo)!.All(o => o.BubbleGroup == "afternoon"),
            "medication-meal group has queue priority over older break-test item");

        var weekly = Create("weekly");
        weekly.Data.Reminders = weekly.Data.Reminders.Where(r => r.Id == "sample-weekly").ToList();
        for (int day = 26; day <= 31; day++) weekly.Tick(Utc($"2026-10-{day:00}T16:00:00Z"), TimeSpan.FromDays(1));
        weekly.Tick(Utc("2026-11-01T16:00:00Z"), zero);
        Check(weekly.Data.Occurrences.Count == 1 && weekly.Data.Occurrences[0].LocalDate.DayOfWeek == DayOfWeek.Wednesday,
            "weekly creates only Wednesday occurrence across all seven weekdays");
        Check(!weekly.MayNotify(weekly.Data.Occurrences[0], Utc("2026-10-29T15:00:00Z")),
            "old Wednesday pending medication is not automatically delivered Thursday");
        weekly = new ReminderScheduler(new ReminderStore(weekly.ConfigurationPath));
        weekly.Tick(Utc("2026-10-28T16:00:00Z"), zero);
        Check(weekly.Data.Occurrences.Count == 1, "weekly restart and backward clock cannot repeat Wednesday");
        weekly.Tick(Utc("2026-11-04T14:59:59Z"), zero);
        Check(weekly.Data.Occurrences.Count == 1, "next Wednesday before 16:00 does not fire");
        weekly.Tick(Utc("2026-11-04T15:00:00Z"), zero);
        Check(weekly.Data.Occurrences.Count == 2 && weekly.LocalNow(weekly.Data.Occurrences[1].DueAtUtc).Hour == 17,
            "next Wednesday at 16:00 fires once");
        weekly.Snooze(weekly.Data.Occurrences[1], Utc("2026-11-04T22:55:00Z"));
        Check(!weekly.MayNotify(weekly.Data.Occurrences[1], Utc("2026-11-04T23:05:00Z")),
            "Wednesday snooze crossing midnight never auto-fires on Thursday");
        var weeklyDst = Create("weekly-dst");
        weeklyDst.Data.Reminders = weeklyDst.Data.Reminders.Where(r => r.Id == "sample-weekly").ToList();
        weeklyDst.Tick(Utc("2026-10-21T14:00:00Z"), zero);
        weeklyDst.Tick(Utc("2026-10-28T15:00:00Z"), zero);
        Check(weeklyDst.Data.Occurrences.Count == 2 && weeklyDst.Data.Occurrences.All(o => weeklyDst.LocalNow(o.DueAtUtc).Hour == 17),
            "Wednesday time remains 16:00 across DST offset change");

        var previews = Create("previews");
        foreach (var id in new[] { "sample-a", "sample-b", "sample-c", "sample-weekly", "test-break" })
        {
            var preview = previews.AddTestGroup(id, night);
            int expected = id is "sample-b" or "sample-c" ? 2 : 1;
            Check(preview.Count == expected && preview.All(o => o.IsManualTest && previews.MayNotify(o, night)), id + " immediate isolated preview");
            foreach (var occurrence in preview) previews.Acknowledge(occurrence, true, night);
        }
        Check(previews.Data.Reminders.All(r => r.LastFiredLocalDate is null), "preview confirmations never change real last-fired dates");
        previews.Tick(atEight, zero);
        Check(previews.Data.Occurrences.Count(o => !o.IsManualTest) == 5 &&
            previews.Data.Occurrences.Where(o => !o.IsManualTest).All(o => o.Acknowledgment == ReminderAcknowledgment.Pending),
            "confirmed test items do not suppress or complete real medication/meal reminders");

        var oldData = SyntheticReminderFixtures.Schedule();
        oldData.Version = 1;
        oldData.Reminders.RemoveAll(r => r.Id == "sample-weekly");
        var oldTimes = new Dictionary<string, TimeOnly>
        {
            ["sample-a"] = new(7,5), ["lunch"] = new(10,35), ["sample-b"] = new(12,15),
            ["dinner"] = new(18,45), ["sample-c"] = new(21,5)
        };
        foreach(var definition in oldData.Reminders.Where(r => oldTimes.ContainsKey(r.Id)))
        { definition.Time = oldTimes[definition.Id]; definition.BubbleGroup = ""; definition.Title += " + упражнения"; }
        oldData.Reminders.First(r => r.Id == "sample-c").Enabled = false;
        oldData.Personality.PersonalityEnabled = false;
        oldData.Reminders.Add(new ReminderDefinition { Id = "custom", Title = "Личное", Time = new(18,0) });
        var migrationNow = Utc("2026-10-31T12:45:00Z");
        oldData.Occurrences.Add(new ReminderOccurrence { Id = "old-done", ReminderId = "sample-a", Title = "Пример A + упражнения",
            LocalDate = new(2026,10,31), DueAtUtc = Utc("2026-10-31T09:15:00Z"), Acknowledgment = ReminderAcknowledgment.Completed,
            AcknowledgedAtUtc = Utc("2026-10-31T09:16:00Z"), RequiresConfirmation = true });
        oldData.Occurrences.Add(new ReminderOccurrence { Id = "old-lunch", ReminderId = "lunch", Title = "Обед",
            LocalDate = new(2026,10,31), DueAtUtc = Utc("2026-10-31T12:30:00Z"), RequiresConfirmation = true });
        oldData.Occurrences.Add(new ReminderOccurrence { Id = "old-pending", ReminderId = "dinner", Title = "Ужин",
            LocalDate = new(2026,10,30), DueAtUtc = Utc("2026-10-30T18:00:00Z"), RequiresConfirmation = true });
        string migrationPath = Path.Combine(folder, "migration.json");
        string originalJson = System.Text.Json.JsonSerializer.Serialize(oldData, ReminderStore.JsonOptions);
        File.WriteAllText(migrationPath, originalJson);
        var migrated = new ReminderScheduler(new ReminderStore(migrationPath), migrationNow);
        Check(migrated.Data.Version == 1 && migrated.Data.Reminders.Count == oldData.Reminders.Count, "legacy version and definition count retained");
        Check(System.Text.Json.JsonSerializer.Serialize(migrated.Data, ReminderStore.JsonOptions) == originalJson, "legacy records retain exact values");
        Check(!migrated.Data.Personality.PersonalityEnabled && !migrated.Data.Reminders.First(r => r.Id == "sample-c").Enabled, "legacy preferences retained");
        Check(migrated.Data.Occurrences.First(o => o.Id == "old-done").AcknowledgedAtUtc == oldData.Occurrences[0].AcknowledgedAtUtc, "acknowledgment retained");
        Check(migrated.Data.Occurrences.First(o => o.Id == "old-lunch").DueAtUtc == oldData.Occurrences[1].DueAtUtc, "existing due instant retained");
        Check(!migrated.Data.Occurrences.First(o => o.Id == "old-pending").IsLegacyHistory, "no silent history reinterpretation");
        Check(File.ReadAllText(migrationPath) == originalJson && !File.Exists(migrationPath + ".bak"), "constructor performs no rewrite");
        Check(migrated.Data.Reminders.Single(r => r.Id == "custom").Time == new TimeOnly(18, 0), "custom schedule retained");
        migrated.Acknowledge(migrated.Data.Occurrences.First(o => o.Id == "old-lunch"), false, migrationNow);
        Check(File.ReadAllText(migrationPath + ".pre-v03-backup") == originalJson, "verified permanent backup before first legacy write");
        migrated = new ReminderScheduler(new ReminderStore(migrationPath), atTwo);
        Check(migrated.Data.Version == 1 && migrated.Data.Reminders.Count == oldData.Reminders.Count, "restart does not install a new schedule");
        Console.WriteLine($"{assertions} scheduling assertions passed. Test JSON: {folder}");
        Assert.Equal(65, assertions);
    }

}

