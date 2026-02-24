namespace GetIdeas.Worker.Options;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gpt-4o-mini";

    public string Endpoint { get; init; } = "https://api.openai.com/v1/chat/completions";

    public int TimeoutSeconds { get; init; } = 60;
}
