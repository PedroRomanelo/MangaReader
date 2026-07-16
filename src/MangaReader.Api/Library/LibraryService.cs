using System.Globalization;
using Dapper;
using MangaReader.Api.Infra;
using MangaReader.Api.MangaDex;

namespace MangaReader.Api.Library;

public sealed class LibraryService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MangaDexClient _mangaDex;
    private readonly MangaDexOptions _mangaDexOptions;
    private readonly ILogger<LibraryService> _logger;

    public LibraryService(
        SqliteConnectionFactory connectionFactory,
        MangaDexClient mangaDex,
        MangaDexOptions mangaDexOptions,
        ILogger<LibraryService> logger)
    {
        _connectionFactory = connectionFactory;
        _mangaDex = mangaDex;
        _mangaDexOptions = mangaDexOptions;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LibraryListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Create();
        var rows = await connection.QueryAsync<LibraryRow>(
            @"SELECT
                m.id                                                                     AS Id,
                m.mangadex_id                                                            AS MangadexId,
                m.title                                                                  AS Title,
                m.cover_filename                                                         AS CoverFilename,
                (SELECT count(*) FROM chapter c WHERE c.manga_id = m.id)                 AS TotalChapters,
                (SELECT count(*) FROM chapter c WHERE c.manga_id = m.id
                                                 AND c.download_status = 'done')        AS DownloadedChapters
              FROM manga m
              ORDER BY m.title;").ConfigureAwait(false);

        return rows.Select(r => new LibraryListItem(
            r.Id,
            r.MangadexId,
            r.Title,
            BuildCoverUrl(r.MangadexId, r.CoverFilename),
            (int)r.TotalChapters,
            (int)r.DownloadedChapters)).ToList();
    }

    public async Task<LibraryMangaAdded> AddAsync(string mangadexId, string language, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.Create();

        var existingId = await connection.ExecuteScalarAsync<long?>(
            "SELECT id FROM manga WHERE mangadex_id = @mangadexId LIMIT 1;",
            new { mangadexId }).ConfigureAwait(false);

        if (existingId.HasValue)
        {
            throw new MangaAlreadyExistsException(existingId.Value, mangadexId);
        }

        var manga = await _mangaDex.GetMangaAsync(mangadexId, language, cancellationToken).ConfigureAwait(false);
        var chapters = await FetchFullFeedAsync(mangadexId, language, cancellationToken).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        var newId = await connection.ExecuteScalarAsync<long>(
            @"INSERT INTO manga (mangadex_id, title, status, year, content_rating, cover_filename)
              VALUES (@MangadexId, @Title, @Status, @Year, @ContentRating, @CoverFilename)
              RETURNING id;",
            new
            {
                MangadexId = mangadexId,
                manga.Title,
                manga.Status,
                manga.Year,
                manga.ContentRating,
                manga.CoverFilename,
            },
            transaction: transaction).ConfigureAwait(false);

        var chapterRows = chapters.Select(c => new
        {
            MangaId = newId,
            MangadexId = c.Id,
            c.Volume,
            c.Chapter,
            SortNumber = ParseSortNumber(c.Chapter),
            c.Title,
            c.Language,
            ScanlationGroup = c.ScanlationGroup,
            PageCount = c.Pages,
            PublishedAt = c.PublishedAt?.ToString("o"),
        }).ToList();

        if (chapterRows.Count > 0)
        {
            await connection.ExecuteAsync(
                @"INSERT INTO chapter
                    (manga_id, mangadex_id, volume, chapter, sort_number, title, language, scanlation_group, page_count, published_at)
                  VALUES
                    (@MangaId, @MangadexId, @Volume, @Chapter, @SortNumber, @Title, @Language, @ScanlationGroup, @PageCount, @PublishedAt);",
                chapterRows,
                transaction: transaction).ConfigureAwait(false);
        }

        transaction.Commit();

        _logger.LogInformation(
            "Manga {MangadexId} ({Title}) adicionado como id={LocalId} com {ChapterCount} capítulos.",
            mangadexId, manga.Title, newId, chapterRows.Count);

        return new LibraryMangaAdded(
            newId,
            mangadexId,
            manga.Title,
            BuildCoverUrl(mangadexId, manga.CoverFilename),
            chapterRows.Count);
    }

    private async Task<List<ChapterFeedItem>> FetchFullFeedAsync(string mangadexId, string language, CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        const int mangaDexOffsetLimit = 10_000;

        var all = new List<ChapterFeedItem>();
        var offset = 0;
        while (offset < mangaDexOffsetLimit)
        {
            var page = await _mangaDex
                .GetFeedAsync(mangadexId, language, limit: pageSize, offset: offset, cancellationToken)
                .ConfigureAwait(false);
            if (page.Count == 0) break;
            all.AddRange(page);
            if (page.Count < pageSize) break;
            offset += page.Count;
        }
        return all;
    }

    private string? BuildCoverUrl(string mangadexId, string? coverFilename)
    {
        if (string.IsNullOrEmpty(coverFilename)) return null;
        return $"{_mangaDexOptions.CoversBaseUrl.TrimEnd('/')}/{mangadexId}/{coverFilename}";
    }

    private static double? ParseSortNumber(string? chapter)
    {
        if (string.IsNullOrWhiteSpace(chapter)) return null;
        return double.TryParse(chapter, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    // count(*) do SQLite volta como Int64 — o record precisa combinar exatamente
    // com a assinatura do ctor pro Dapper materializar via ctor de record.
    private sealed record LibraryRow(
        long Id,
        string MangadexId,
        string Title,
        string? CoverFilename,
        long TotalChapters,
        long DownloadedChapters);
}
