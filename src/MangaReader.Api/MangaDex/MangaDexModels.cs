namespace MangaReader.Api.MangaDex;

public sealed record MangaSearchResult(
    string Id,
    string Title,
    int? Year,
    string? Status,
    string? ContentRating,
    string? CoverFilename);

public sealed record ChapterFeedItem(
    string Id,
    string? Volume,
    string? Chapter,
    string? Title,
    string Language,
    string? ScanlationGroup,
    int Pages,
    DateTimeOffset? PublishedAt);

public sealed record AtHomeServer(
    string BaseUrl,
    string Hash,
    IReadOnlyList<string> Data,
    IReadOnlyList<string> DataSaver);
