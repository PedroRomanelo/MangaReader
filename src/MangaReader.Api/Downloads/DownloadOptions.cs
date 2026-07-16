namespace MangaReader.Api.Downloads;

public sealed class DownloadOptions
{
    public const string SectionName = "Downloads";

    public string LibraryRoot { get; set; } = "";

    // "data" (resolução cheia) ou "data-saver" (menor). Usado quando o
    // request não especifica quality no body.
    public string DefaultQuality { get; set; } = "data";
}
