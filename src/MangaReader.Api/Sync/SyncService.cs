using System.Globalization;
using Dapper;
using MangaReader.Api.Infra;

namespace MangaReader.Api.Sync;

public sealed class SyncService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SyncService> _logger;

    public SyncService(SqliteConnectionFactory connectionFactory, ILogger<SyncService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SyncManifestManga>> GetManifestAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Create();
        var rows = await connection.QueryAsync<ManifestRow>(
            @"SELECT m.id             AS MangaLocalId,
                     m.mangadex_id    AS MangaMangadexId,
                     m.title          AS Title,
                     c.id             AS ChapterLocalId,
                     c.mangadex_id    AS ChapterMangadexId,
                     c.chapter        AS ChapterLabel,
                     c.download_status AS DownloadStatus,
                     c.local_path     AS LocalPath,
                     c.file_size_bytes AS FileSizeBytes,
                     c.sort_number    AS SortNumber
              FROM manga m
              JOIN chapter c ON c.manga_id = m.id
              ORDER BY m.title, c.sort_number IS NULL, c.sort_number;").ConfigureAwait(false);

        return rows
            .GroupBy(r => (r.MangaLocalId, r.MangaMangadexId, r.Title))
            .Select(g => new SyncManifestManga(
                g.Key.MangaLocalId,
                g.Key.MangaMangadexId,
                g.Key.Title,
                g.Select(r => new SyncManifestChapter(
                    r.ChapterLocalId,
                    r.ChapterMangadexId,
                    r.ChapterLabel,
                    r.DownloadStatus,
                    HasFile: r.LocalPath is not null && r.DownloadStatus == "done",
                    FileSize: r.FileSizeBytes)).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<SyncProgressItem>> GetProgressAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Create();
        var rows = await connection.QueryAsync<ProgressRow>(
            @"SELECT c.mangadex_id  AS ChapterMangadexId,
                     rp.last_page   AS LastPage,
                     rp.is_read     AS IsRead,
                     rp.updated_at  AS UpdatedAt
              FROM reading_progress rp
              JOIN chapter c ON c.id = rp.chapter_id
              ORDER BY rp.updated_at DESC;").ConfigureAwait(false);

        return rows.Select(r => new SyncProgressItem(
            r.ChapterMangadexId,
            (int)r.LastPage,
            r.IsRead != 0,
            ParseTimestamp(r.UpdatedAt))).ToList();
    }

    public async Task<int> MergeProgressAsync(IReadOnlyList<SyncProgressItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return 0;

        using var connection = _connectionFactory.Create();
        using var transaction = connection.BeginTransaction();

        var merged = 0;
        foreach (var item in items)
        {
            var updatedAtUtc = item.UpdatedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            var affected = await connection.ExecuteAsync(
                @"INSERT INTO reading_progress (chapter_id, last_page, is_read, updated_at)
                  SELECT c.id, @LastPage, @IsRead, @UpdatedAt
                    FROM chapter c
                    WHERE c.mangadex_id = @ChapterMangadexId
                  ON CONFLICT(chapter_id) DO UPDATE
                    SET last_page  = excluded.last_page,
                        is_read    = excluded.is_read,
                        updated_at = excluded.updated_at
                    WHERE excluded.updated_at > reading_progress.updated_at;",
                new
                {
                    item.ChapterMangadexId,
                    item.LastPage,
                    IsRead = item.IsRead ? 1 : 0,
                    UpdatedAt = updatedAtUtc,
                },
                transaction: transaction).ConfigureAwait(false);
            merged += affected;
        }

        transaction.Commit();
        _logger.LogInformation("Sync progress: {Merged}/{Received} entradas aplicadas.", merged, items.Count);
        return merged;
    }

    private static DateTimeOffset ParseTimestamp(string raw)
    {
        // Sempre gravamos com .ToString("o") em UTC, então o parse é direto.
        // Tolerante caso alguém tenha metido `datetime('now')` (sem T) na
        // coluna manualmente.
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto;
        }
        return DateTimeOffset.MinValue;
    }

    private sealed record ManifestRow(
        long MangaLocalId,
        string MangaMangadexId,
        string Title,
        long ChapterLocalId,
        string ChapterMangadexId,
        string? ChapterLabel,
        string DownloadStatus,
        string? LocalPath,
        long? FileSizeBytes,
        double? SortNumber);

    private sealed record ProgressRow(
        string ChapterMangadexId,
        long LastPage,
        long IsRead,
        string UpdatedAt);
}
