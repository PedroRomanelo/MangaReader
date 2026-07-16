using System.Diagnostics;
using System.Net;
using Dapper;
using MangaReader.Api.Infra;
using MangaReader.Api.MangaDex;

namespace MangaReader.Api.Downloads;

public sealed class DownloadWorker : BackgroundService
{
    private readonly ILogger<DownloadWorker> _logger;
    private readonly DownloadQueue _queue;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly MangaDexClient _mangaDex;
    private readonly DownloadOptions _options;
    private readonly MangaDexOptions _mangaDexOptions;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AtHomeReporter _reporter;

    public DownloadWorker(
        ILogger<DownloadWorker> logger,
        DownloadQueue queue,
        SqliteConnectionFactory connectionFactory,
        MangaDexClient mangaDex,
        DownloadOptions options,
        MangaDexOptions mangaDexOptions,
        IHttpClientFactory httpFactory,
        AtHomeReporter reporter)
    {
        _logger = logger;
        _queue = queue;
        _connectionFactory = connectionFactory;
        _mangaDex = mangaDex;
        _options = options;
        _mangaDexOptions = mangaDexOptions;
        _httpFactory = httpFactory;
        _reporter = reporter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DownloadWorker iniciado. LibraryRoot={LibraryRoot}", _options.LibraryRoot);

        await foreach (var request in _queue.ConsumeAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao baixar chapter id={ChapterId}", request.ChapterId);
                _queue.Fail(request.ChapterId, ex.Message);
                try { await SetStatusAsync(request.ChapterId, "error", stoppingToken); }
                catch (Exception dbEx) { _logger.LogWarning(dbEx, "Falha ao marcar chapter {Id} como error.", request.ChapterId); }
            }
        }
    }

    private async Task ProcessAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        var chapter = await LoadChapterAsync(request.ChapterId);
        if (chapter is null)
        {
            _logger.LogWarning("Chapter id={Id} não existe mais no DB — pulando.", request.ChapterId);
            _queue.Complete(request.ChapterId);
            return;
        }

        _queue.MarkDownloading(request.ChapterId, (int)Math.Max(1, chapter.PageCount));
        await SetStatusAsync(request.ChapterId, "downloading", cancellationToken);

        var atHome = await _mangaDex.GetAtHomeServerAsync(chapter.MangadexId, cancellationToken);
        var qualityPath = request.Quality == DownloadQuality.DataSaver ? "data-saver" : "data";
        var files = request.Quality == DownloadQuality.DataSaver ? atHome.DataSaver : atHome.Data;

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"Chapter {chapter.MangadexId} não tem páginas em '{qualityPath}' (provavelmente hospedado externamente, ex.: MangaPlus).");
        }

        _queue.MarkDownloading(request.ChapterId, files.Count);

        var localPath = BuildLocalPath(chapter);
        using var writer = new CbzWriter(localPath);
        var http = _httpFactory.CreateClient();
        // O CDN @Home rejeita requisições sem User-Agent (400 Bad Request).
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(_mangaDexOptions.UserAgent);

        var baseUrl = atHome.BaseUrl;
        var hash = atHome.Hash;

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filename = files[i];
            var extension = Path.GetExtension(filename);

            var (bytes, elapsedMs) = await DownloadPageAsync(
                http,
                chapter.MangadexId,
                request.Quality,
                baseUrl,
                hash,
                qualityPath,
                filename,
                onBaseUrlExpired: async ct =>
                {
                    _logger.LogInformation(
                        "baseUrl expirado para chapter {MangadexId} — refazendo /at-home/server.",
                        chapter.MangadexId);
                    var fresh = await _mangaDex.GetAtHomeServerAsync(chapter.MangadexId, ct);
                    baseUrl = fresh.BaseUrl;
                    hash = fresh.Hash;
                    return fresh;
                },
                cancellationToken);

            writer.AddPage(i + 1, extension, bytes);
            _queue.ReportPageDone(request.ChapterId, i + 1);
        }

        var fileSize = writer.FinalizeAndMove();
        await MarkDoneAsync(request.ChapterId, localPath, fileSize, cancellationToken);
        _queue.Complete(request.ChapterId);

        _logger.LogInformation(
            "Chapter id={ChapterId} ({MangadexId}) → {LocalPath} ({Bytes} bytes, {Pages} páginas)",
            chapter.Id, chapter.MangadexId, localPath, fileSize, files.Count);
    }

    private async Task<(byte[] Bytes, long ElapsedMs)> DownloadPageAsync(
        HttpClient http,
        string chapterMangadexId,
        DownloadQuality quality,
        string baseUrl,
        string hash,
        string qualityPath,
        string filename,
        Func<CancellationToken, Task<AtHomeServer>> onBaseUrlExpired,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        Exception? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var url = $"{baseUrl.TrimEnd('/')}/{qualityPath}/{hash}/{filename}";
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (resp.StatusCode == HttpStatusCode.Forbidden)
                {
                    stopwatch.Stop();
                    _reporter.FireReport(url, success: false, bytes: 0, durationMs: stopwatch.ElapsedMilliseconds, cached: false);
                    // baseUrl provavelmente expirou — pega novo e refaz esta iteração.
                    var refreshed = await onBaseUrlExpired(cancellationToken);
                    baseUrl = refreshed.BaseUrl;
                    hash = refreshed.Hash;
                    continue;
                }

                resp.EnsureSuccessStatusCode();

                var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                stopwatch.Stop();

                var cached = resp.Headers.TryGetValues("X-Cache", out var xcache)
                             && xcache.Any(v => v.Contains("HIT", StringComparison.OrdinalIgnoreCase));
                _reporter.FireReport(url, success: true, bytes: bytes.Length, durationMs: stopwatch.ElapsedMilliseconds, cached: cached);

                return (bytes, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                stopwatch.Stop();
                lastError = ex;
                _reporter.FireReport(url, success: false, bytes: 0, durationMs: stopwatch.ElapsedMilliseconds, cached: false);

                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Falha ao baixar {Url} — retry {Next}/{Max} em {DelayMs}ms",
                    url, attempt + 1, maxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Não consegui baixar {filename} de chapter {chapterMangadexId} após {maxAttempts} tentativas.",
            lastError);
    }

    private string BuildLocalPath(ChapterRow chapter)
    {
        return Path.Combine(_options.LibraryRoot, chapter.MangaId.ToString(), $"{chapter.Id}.cbz");
    }

    private async Task<ChapterRow?> LoadChapterAsync(long chapterId)
    {
        using var conn = _connectionFactory.Create();
        return await conn.QueryFirstOrDefaultAsync<ChapterRow?>(
            @"SELECT c.id             AS Id,
                     c.manga_id       AS MangaId,
                     c.mangadex_id    AS MangadexId,
                     c.page_count     AS PageCount
              FROM chapter c
              WHERE c.id = @Id
              LIMIT 1;",
            new { Id = chapterId });
    }

    private async Task SetStatusAsync(long chapterId, string status, CancellationToken cancellationToken)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(
            "UPDATE chapter SET download_status = @Status WHERE id = @Id;",
            new { Id = chapterId, Status = status });
    }

    private async Task MarkDoneAsync(long chapterId, string localPath, long fileSizeBytes, CancellationToken cancellationToken)
    {
        using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(
            @"UPDATE chapter
              SET download_status = 'done',
                  local_path      = @LocalPath,
                  file_size_bytes = @FileSize,
                  downloaded_at   = datetime('now')
              WHERE id = @Id;",
            new { Id = chapterId, LocalPath = localPath, FileSize = fileSizeBytes });
    }

    private sealed record ChapterRow(long Id, long MangaId, string MangadexId, long PageCount);
}
