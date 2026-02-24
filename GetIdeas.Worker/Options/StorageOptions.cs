namespace GetIdeas.Worker.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ConnectionString { get; init; } = "Data Source=getideas.db";
}
