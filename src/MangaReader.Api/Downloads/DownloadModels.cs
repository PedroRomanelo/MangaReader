namespace MangaReader.Api.Downloads;

public enum DownloadQuality
{
    Data,
    DataSaver,
}

public enum DownloadState
{
    Queued,
    Downloading,
    Failed,
}

public sealed record DownloadProgress(
    long ChapterId,
    DownloadState State,
    int Done,
    int Total,
    string? Error,
    DateTimeOffset UpdatedAt);

// Body dos endpoints (opcionais — client pode mandar {} ou omitir).
public sealed record DownloadChapterRequest(string? Quality);
public sealed record DownloadMangaRequest(bool? OnlyMissing, string? Quality);

// Respostas
public sealed record ChapterEnqueuedResponse(long ChapterId, string Quality, string StatusUrl);
public sealed record MangaEnqueuedResponse(long MangaId, int Enqueued, string Quality);

public sealed record ChapterStatusResponse(
    long ChapterId,
    string Status,             // 'none' | 'queued' | 'downloading' | 'done' | 'error'
    int? Done,
    int? Total,
    string? Error,
    string? LocalPath,
    long? FileSizeBytes);

public sealed record DownloadsSnapshotResponse(
    IReadOnlyList<DownloadProgress> Queued,
    IReadOnlyList<DownloadProgress> Downloading,
    IReadOnlyList<DownloadProgress> Failed);

// Interno — item da fila
internal sealed record DownloadRequest(long ChapterId, DownloadQuality Quality);

public static class DownloadQualityExtensions
{
    public static DownloadQuality Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DownloadQuality.Data;
        return raw.Trim().ToLowerInvariant() switch
        {
            "data" => DownloadQuality.Data,
            "data-saver" or "datasaver" => DownloadQuality.DataSaver,
            _ => throw new ArgumentException($"Quality inválida: '{raw}'. Use 'data' ou 'data-saver'."),
        };
    }

    public static string ToWireValue(this DownloadQuality quality) => quality switch
    {
        DownloadQuality.DataSaver => "data-saver",
        _ => "data",
    };
}
