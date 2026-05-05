namespace ChatApp.Infrastructure.Options;

public sealed class TemporaryStoreOptions
{
    public const string SectionName = "TemporaryStore";

    public string Provider { get; set; } = "InMemory";
    public string KeyPrefix { get; set; } = "chatapp";
}
