namespace MangaReader.Api.Sync;

public sealed record SyncManifestManga(
    long MangaId,
    string MangadexId,
    string Title,
    IReadOnlyList<SyncManifestChapter> Chapters);

public sealed record SyncManifestChapter(
    long ChapterId,
    string MangadexId,
    string? Chapter,
    string DownloadStatus,
    bool HasFile,
    long? FileSize,
    int PageCount);

public sealed record SyncProgressItem(
    string ChapterMangadexId,
    int LastPage,
    bool IsRead,
    DateTimeOffset UpdatedAt);

public sealed record SyncMergeResponse(int Received, int Merged);
