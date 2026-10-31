using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChihDesktop;

// Shared schema/clone rules, with no file access or infrastructure dependency.
public static class ReminderDataCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;

    public static void Validate(ReminderData data)
    {
        if (data.Version is not (1 or 2 or 3) || !AppTimeZones.IsValid(data.TimeZoneId) || data.Reminders is null || data.Occurrences is null || data.Personality is null ||
            data.Reminders.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title) ||
                !Enum.IsDefined(r.Recurrence) || !Enum.IsDefined(r.WeeklyDay) || r.IntervalMinutes is < 1 or > 1440 ||
                (r.TimeZoneId is not null && !AppTimeZones.IsValid(r.TimeZoneId)) ||
                r.MessageVariants is null || r.MessageVariants.Count == 0 || r.MessageVariants.Any(string.IsNullOrWhiteSpace)) ||
            data.Reminders.Select(r => r.Id).Distinct().Count() != data.Reminders.Count ||
            data.Occurrences.Any(o => o is null || string.IsNullOrWhiteSpace(o.Id) || !Enum.IsDefined(o.Acknowledgment) || o.TimeZoneId is not null && !AppTimeZones.IsValid(o.TimeZoneId)) ||
            data.Occurrences.Select(o => o.Id).Distinct().Count() != data.Occurrences.Count ||
            data.Personality.MinimumCommentIntervalMinutes is < 1 or > 1440)
            throw new JsonException("Invalid reminder configuration or duplicate IDs.");
    }
}

