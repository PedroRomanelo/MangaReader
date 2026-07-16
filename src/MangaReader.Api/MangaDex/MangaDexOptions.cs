namespace MangaReader.Api.MangaDex;

public sealed class MangaDexOptions
{
    public const string SectionName = "MangaDex";

    public string BaseUrl { get; set; } = "https://api.mangadex.org";
    public string CoversBaseUrl { get; set; } = "https://uploads.mangadex.org/covers";
    public string ReportUrl { get; set; } = "https://api.mangadex.network/report";
    public string UserAgent { get; set; } = "";
}
