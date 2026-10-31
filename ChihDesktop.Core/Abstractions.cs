namespace ChihDesktop;

public enum ReminderLoadStatus { NewStore, Primary, RecoveredBackup, BlockedUnsupported, BlockedCorrupt, BlockedIo }
public sealed record ReminderLoadResult(ReminderLoadStatus Status, ReminderData? Data, string? Message, bool CanWrite);

public interface IReminderPersistence
{
    string FilePath { get; }
    string? Error { get; }
    bool CanWrite { get; }
    ReminderLoadResult? LoadOutcome { get; }
    ReminderLoadResult Load();
    bool Save(ReminderData data);
}

public interface IReminderCommands
{
    event Action? Changed;
    event Action<string>? OperationFailed;
    string? PersistenceError { get; }
    string? StartupWarning { get; }
    ReminderLoadStatus LoadStatus { get; }
    string ConfigurationPath { get; }
    ReminderData GetSnapshot();
    PersonalityOptions GetPersonalityOptions();
    DateTime LocalNow(DateTimeOffset now);
    DateOnly LocalDate(DateTimeOffset now);
    bool IsQuiet(DateTimeOffset now);
    bool MayNotify(ReminderOccurrence occurrence, DateTimeOffset now);
    bool HasWaitingNotification(DateTimeOffset now);
    void Tick(DateTimeOffset now, TimeSpan elapsed);
    ReminderCommandResult Acknowledge(string id, bool completed, DateTimeOffset now);
    ReminderCommandResult Acknowledge(string id, bool completed);
    ReminderCommandResult Snooze(string id, DateTimeOffset now);
    ReminderCommandResult Snooze(string id);
    ReminderCommandResult UpdateSettings(IReadOnlyList<ReminderDefinition> edits, bool quietEnabled, TimeOnly start, TimeOnly end);
    ReminderCommandResult UpdatePersonality(PersonalityOptions options);
    ReminderCommandResult CreateReminder(ReminderDefinition definition);
    ReminderCommandResult DeleteReminder(string id);
    IReadOnlyList<ReminderOccurrence>? AddTestGroup(string id, DateTimeOffset now);
    IReadOnlyList<ReminderOccurrence>? TakeNextGroup(DateTimeOffset now);
    IReadOnlyList<ReminderOccurrence> PresentationGroup(ReminderOccurrence seed, DateTimeOffset now);
    ReminderCommandResult MarkPresented(IReadOnlyList<ReminderOccurrence> group, DateTimeOffset now);
    IReadOnlyList<ReminderOccurrence> GetPresentation();
    void ClosePresentation(IReadOnlyList<string>? expectedIds = null);
}

// Presentation capability only: the Core contract contains no Window/Dispatcher/bitmap types.
public interface IReminderPresenter : IDisposable
{
    event Action<ReminderOccurrence, string>? ActionRequested;
    event Action? DialogueDismissRequested;
    IReadOnlyList<ReminderOccurrence> Occurrences { get; }
    bool IsVisible { get; }
    bool IsDialogue { get; }
    bool IsStoreNotice { get; }
    void Present(IReadOnlyList<ReminderOccurrence> occurrences, string displayMessage);
    void PresentDialogue(string mood, string text);
    void PresentStoreNotice(string text);
    void ShowPersistenceError(string? error);
    void RefreshItems(IReadOnlyList<ReminderOccurrence>? latest = null);
    void HideReminder();
}

