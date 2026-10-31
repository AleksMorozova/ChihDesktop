using System.Text.Json.Serialization;

namespace ChihDesktop;

public sealed class PersonalityOptions
{
    [JsonPropertyName("personalityEnabled")]
    public bool PersonalityEnabled { get; set; } = true;
    [JsonPropertyName("spontaneousCommentsEnabled")]
    public bool SpontaneousCommentsEnabled { get; set; } = true;
    [JsonPropertyName("minimumCommentIntervalMinutes")]
    public int MinimumCommentIntervalMinutes { get; set; } = 20;
    [JsonPropertyName("quietHoursEnabled")]
    public bool QuietHoursEnabled { get; set; } = true;
    [JsonPropertyName("quietStart")]
    public TimeOnly QuietStart { get; set; } = new(22, 0);
    [JsonPropertyName("quietEnd")]
    public TimeOnly QuietEnd { get; set; } = new(8, 0);
}

