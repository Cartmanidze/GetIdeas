namespace GetIdeas.Worker.Options;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    public int PollIntervalMinutes { get; init; } = 360;

    public int MinWordCount { get; init; } = 20;

    public bool RequireKeywordMatch { get; init; }

    public int MinimumConfidenceToNotify { get; init; } = 6;

    public List<string> StopPhrases { get; init; } = ["[removed]", "[deleted]"];
}
