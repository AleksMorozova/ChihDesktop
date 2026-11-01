namespace ChihDesktop;

public enum ChihMood { Content, Playful, Sleepy, Demanding, Curious }

// Small, UI-independent mood model and shuffle bags. Only preferences are persisted;
// affection, cooldowns and current mood intentionally reset on a fresh application run.
public sealed class PersonalityService
{
    private readonly Func<PersonalityOptions> options;
    private readonly Random random;
    private readonly Func<string?> ownerName;
    private readonly Dictionary<DialogueCategory, Queue<string>> bags = new();
    private readonly Dictionary<DialogueCategory, string> previous = new();
    private DateTimeOffset lastMoodChange;
    private DateTimeOffset lastPet;
    private DateTimeOffset lastPetReply = DateTimeOffset.MinValue;
    private DateTimeOffset lastSpeech;
    private DateTimeOffset nextSpontaneous;
    private double affection = .5, energy = .6, attention = .2;
    public ChihMood Mood { get; private set; } = ChihMood.Content;
    public bool Enabled => options().PersonalityEnabled;
    public string MoodLabel => Mood switch
    {
        ChihMood.Playful => "игривый", ChihMood.Sleepy => "сонный", ChihMood.Demanding => "требует внимания",
        ChihMood.Curious => "любопытный", _ => "довольный"
    };

    public PersonalityService(Func<PersonalityOptions> options, DateTimeOffset now, Random? random = null, Func<string?>? ownerName = null)
    {
        this.options = options;
        this.random = random ?? Random.Shared;
        this.ownerName = ownerName ?? (() => null);
        lastMoodChange = lastPet = now;
        lastSpeech = now.AddMinutes(-Math.Max(1, options().MinimumCommentIntervalMinutes));
        nextSpontaneous = now.AddMinutes(Math.Max(1, options().MinimumCommentIntervalMinutes));
    }

    public bool IsQuiet(DateTime local, bool reminderQuiet)
    {
        if (reminderQuiet) return true;
        var config = options();
        if (!config.QuietHoursEnabled) return false;
        var time = TimeOnly.FromDateTime(local);
        return config.QuietStart == config.QuietEnd || (config.QuietStart < config.QuietEnd
            ? time >= config.QuietStart && time < config.QuietEnd : time >= config.QuietStart || time < config.QuietEnd);
    }

    public void Update(DateTimeOffset now, DateTime local, TimeSpan elapsed, TimeSpan userIdle, CatState state)
    {
        if (!Enabled) return;
        // Suspend or clock jumps must not produce an abrupt mood or a backlog of remarks.
        if (elapsed > TimeSpan.FromSeconds(30)) { DeferSpontaneous(now); return; }
        double seconds = Math.Clamp(elapsed.TotalSeconds, 0, 30);
        bool night = local.Hour >= 22 || local.Hour < 8;
        double targetEnergy = night || state == CatState.Sleeping ? .08 : local.Hour is >= 13 and < 16 ? .4 : .75;
        energy += (targetEnergy - energy) * (1 - Math.Exp(-seconds / 300));
        affection = Math.Max(0, affection - seconds / 14400);
        attention = Math.Min(1, attention + seconds / 3600);
        if (now - lastMoodChange < TimeSpan.FromMinutes(3)) return;
        ChihMood target = energy < .3 ? ChihMood.Sleepy
            : now - lastPet < TimeSpan.FromMinutes(2) && affection > .55 ? ChihMood.Playful
            : now - lastPet >= TimeSpan.FromMinutes(40) && attention > .55 ? ChihMood.Demanding
            : userIdle >= TimeSpan.FromMinutes(8) && energy > .45 ? ChihMood.Curious : ChihMood.Content;
        if (target != Mood) { Mood = target; lastMoodChange = now; }
    }

    public void Pet(DateTimeOffset now)
    {
        if (!Enabled) return;
        lastPet = now;
        affection = Math.Min(1, affection + .15);
        attention = Math.Max(0, attention - .25);
        energy = Math.Min(1, energy + .08);
    }

    public string? PetReply(DateTimeOffset now, bool allowed, bool requested = false)
    {
        if (!Enabled || !allowed || now - lastPetReply < TimeSpan.FromSeconds(requested ? 3 : 45) ||
            now - lastSpeech < TimeSpan.FromSeconds(requested ? 3 : 30) || !requested && random.NextDouble() >= .45) return null;
        lastPetReply = now;
        RecordSpeech(now);
        return NextPhrase(DialogueCategory.Petting);
    }

    public string? Spontaneous(DateTimeOffset now, bool allowed)
    {
        var config = options();
        if (!Enabled || !config.SpontaneousCommentsEnabled || !allowed || now < nextSpontaneous ||
            now - lastSpeech < TimeSpan.FromMinutes(Math.Max(1, config.MinimumCommentIntervalMinutes))) return null;
        var category = Mood switch
        {
            ChihMood.Sleepy => DialogueCategory.Sleep, ChihMood.Demanding => DialogueCategory.Petting,
            ChihMood.Curious => DialogueCategory.Coding, _ => random.Next(2) == 0 ? DialogueCategory.Random : DialogueCategory.Coding
        };
        RecordSpeech(now);
        return NextPhrase(category);
    }

    public string? RequestedPhrase(DateTimeOffset now, bool allowed, bool randomPhrase)
    {
        if (!Enabled || !allowed || now - lastSpeech < TimeSpan.FromSeconds(3)) return null;
        var category = randomPhrase ? DialogueCategory.Random : Mood switch
        {
            ChihMood.Sleepy => DialogueCategory.Sleep, ChihMood.Demanding or ChihMood.Playful => DialogueCategory.Petting,
            ChihMood.Curious => DialogueCategory.Coding, _ => DialogueCategory.Random
        };
        RecordSpeech(now);
        return NextPhrase(category);
    }

    public void RecordSpeech(DateTimeOffset now)
    {
        lastSpeech = now;
        DeferSpontaneous(now);
    }

    public string? TaskComment(DateTimeOffset now, DialogueCategory category)
    {
        if (!Enabled || now - lastSpeech < TimeSpan.FromMinutes(Math.Max(1, options().MinimumCommentIntervalMinutes))) return null;
        var phrase = NextPhrase(category); RecordSpeech(now); return phrase;
    }

    public void DeferSpontaneous(DateTimeOffset now) => nextSpontaneous = now.AddMinutes(
        Math.Max(1, options().MinimumCommentIntervalMinutes) + random.NextDouble() * 5);

    public string ReminderMessage(ReminderOccurrence occurrence, bool allowed)
    {
        if (!Enabled || !allowed) return occurrence.Message;
        var category = occurrence.Title.Contains("лекар", StringComparison.OrdinalIgnoreCase) ? DialogueCategory.Medication
            : occurrence.Title.Contains("упражнен", StringComparison.OrdinalIgnoreCase) ? DialogueCategory.Exercise
            : occurrence.Title.Contains("обед", StringComparison.OrdinalIgnoreCase) ? DialogueCategory.Lunch
            : occurrence.Title.Contains("ужин", StringComparison.OrdinalIgnoreCase) ? DialogueCategory.Dinner : DialogueCategory.Random;
        // The UI always retains the exact task title and original controls. This method
        // returns display text only and never mutates the occurrence or its acknowledgment.
        string text = occurrence.Message + "\n" + NextPhrase(category);
        if (category == DialogueCategory.Medication && occurrence.Title.Contains("упражнен", StringComparison.OrdinalIgnoreCase))
            text += "\n" + NextPhrase(DialogueCategory.Exercise);
        return text;
    }

    public string NextPhrase(DialogueCategory category)
    {
        if (!bags.TryGetValue(category, out var bag) || bag.Count == 0)
        {
            var phrases = DialogueCatalog.Phrases[category].ToArray();
            for (int i = phrases.Length - 1; i > 0; i--)
            { int j = random.Next(i + 1); (phrases[i], phrases[j]) = (phrases[j], phrases[i]); }
            if (phrases.Length > 1 && previous.GetValueOrDefault(category) == phrases[0])
                (phrases[0], phrases[1]) = (phrases[1], phrases[0]);
            bags[category] = bag = new Queue<string>(phrases);
        }
        string phrase = bag.Dequeue();
        previous[category] = phrase;
        return DialogueTemplate.Render(phrase, ownerName());
    }
}

