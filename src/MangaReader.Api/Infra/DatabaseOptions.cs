namespace MangaReader.Api.Infra;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "";
}
