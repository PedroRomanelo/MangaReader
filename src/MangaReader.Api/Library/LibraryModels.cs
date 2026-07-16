namespace MangaReader.Api.Library;

public sealed record AddMangaRequest(string MangadexId, string Language);

public sealed record SearchResponseItem(
    string MangadexId,
    string Title,
    int? Year,
    string? Status,
    string? CoverUrl);

public sealed record LibraryListItem(
    long Id,
    string MangadexId,
    string Title,
    string? CoverUrl,
    int TotalChapters,
    int DownloadedChapters);

public sealed record LibraryMangaAdded(
    long Id,
    string MangadexId,
    string Title,
    string? CoverUrl,
    int TotalChapters);

public sealed class MangaAlreadyExistsException : Exception
{
    public long ExistingId { get; }
    public string MangadexId { get; }

    public MangaAlreadyExistsException(long existingId, string mangadexId)
        : base($"Mangá {mangadexId} já está na biblioteca (id local {existingId}).")
    {
        ExistingId = existingId;
        MangadexId = mangadexId;
    }
}
