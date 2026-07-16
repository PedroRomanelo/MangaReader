using Dapper;
using MangaReader.Api.Infra;

namespace MangaReader.Api.Downloads;

public static class DownloadEndpoints
{
    public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/chapters/{id}/download — enfileira um único capítulo.
        app.MapPost("/api/chapters/{id:long}/download", async (
            long id,
            SqliteConnectionFactory connections,
            DownloadQueue queue,
            DownloadOptions options,
            DownloadChapterRequest? body,
            CancellationToken cancellationToken) =>
        {
            DownloadQuality quality;
            try
            {
                quality = DownloadQualityExtensions.Parse(body?.Quality ?? options.DefaultQuality);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            using var conn = connections.Create();
            var chapter = await conn.QueryFirstOrDefaultAsync<ChapterQueueRow>(
                @"SELECT id AS Id, page_count AS PageCount, download_status AS DownloadStatus
                  FROM chapter WHERE id = @Id LIMIT 1;",
                new { Id = id });

            if (chapter is null) return Results.NotFound(new { message = $"Chapter {id} não encontrado." });

            if (chapter.DownloadStatus == "done")
            {
                return Results.Ok(new { message = "Já baixado.", chapterId = id, status = "done" });
            }

            await conn.ExecuteAsync(
                "UPDATE chapter SET download_status = 'downloading' WHERE id = @Id;",
                new { Id = id });

            queue.Enqueue(id, quality, (int)chapter.PageCount);

            return Results.Accepted(
                uri: $"/api/chapters/{id}/status",
                value: new ChapterEnqueuedResponse(id, quality.ToWireValue(), $"/api/chapters/{id}/status"));
        });

        // POST /api/manga/{id}/download — enfileira em lote.
        app.MapPost("/api/manga/{id:long}/download", async (
            long id,
            SqliteConnectionFactory connections,
            DownloadQueue queue,
            DownloadOptions options,
            DownloadMangaRequest? body,
            CancellationToken cancellationToken) =>
        {
            DownloadQuality quality;
            try
            {
                quality = DownloadQualityExtensions.Parse(body?.Quality ?? options.DefaultQuality);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            var onlyMissing = body?.OnlyMissing ?? true;

            using var conn = connections.Create();
            var mangaExists = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM manga WHERE id = @Id LIMIT 1;", new { Id = id });
            if (mangaExists is null)
            {
                return Results.NotFound(new { message = $"Manga {id} não encontrado." });
            }

            var whereClause = onlyMissing
                ? "manga_id = @Id AND download_status IN ('none','error')"
                : "manga_id = @Id AND download_status != 'done'";

            var chapters = (await conn.QueryAsync<ChapterQueueRow>(
                $@"SELECT id AS Id, page_count AS PageCount, download_status AS DownloadStatus
                   FROM chapter
                   WHERE {whereClause}
                   ORDER BY sort_number IS NULL, sort_number;",
                new { Id = id })).ToList();

            if (chapters.Count == 0)
            {
                return Results.Ok(new MangaEnqueuedResponse(id, 0, quality.ToWireValue()));
            }

            await conn.ExecuteAsync(
                $"UPDATE chapter SET download_status = 'downloading' WHERE {whereClause};",
                new { Id = id });

            foreach (var c in chapters)
            {
                queue.Enqueue(c.Id, quality, (int)c.PageCount);
            }

            return Results.Accepted(
                uri: null,
                value: new MangaEnqueuedResponse(id, chapters.Count, quality.ToWireValue()));
        });

        // GET /api/chapters/{id}/status — o app faz polling aqui.
        app.MapGet("/api/chapters/{id:long}/status", async (
            long id,
            SqliteConnectionFactory connections,
            DownloadQueue queue,
            CancellationToken cancellationToken) =>
        {
            using var conn = connections.Create();
            var row = await conn.QueryFirstOrDefaultAsync<ChapterStatusRow>(
                @"SELECT id AS Id,
                         download_status AS DownloadStatus,
                         local_path      AS LocalPath,
                         file_size_bytes AS FileSizeBytes,
                         page_count      AS PageCount
                  FROM chapter WHERE id = @Id LIMIT 1;",
                new { Id = id });
            if (row is null) return Results.NotFound(new { message = $"Chapter {id} não encontrado." });

            var live = queue.TryGet(id);
            if (live is not null)
            {
                var status = live.State switch
                {
                    DownloadState.Queued => "queued",
                    DownloadState.Downloading => "downloading",
                    DownloadState.Failed => "error",
                    _ => "unknown",
                };
                return Results.Ok(new ChapterStatusResponse(
                    id, status, live.Done, live.Total, live.Error, row.LocalPath, row.FileSizeBytes));
            }

            // Sem estado ao vivo: fonte da verdade é o DB.
            return Results.Ok(new ChapterStatusResponse(
                id,
                row.DownloadStatus,
                Done: row.DownloadStatus == "done" ? (int)row.PageCount : (int?)null,
                Total: (int)row.PageCount,
                Error: null,
                LocalPath: row.LocalPath,
                FileSizeBytes: row.FileSizeBytes));
        });

        // GET /api/downloads — snapshot pra debugging/monitoramento.
        app.MapGet("/api/downloads", (DownloadQueue queue) => Results.Ok(queue.Snapshot()));

        // DELETE /api/chapters/{id}/file — apaga o .cbz e volta status pra 'none'.
        app.MapDelete("/api/chapters/{id:long}/file", async (
            long id,
            SqliteConnectionFactory connections,
            DownloadQueue queue,
            CancellationToken cancellationToken) =>
        {
            using var conn = connections.Create();
            var row = await conn.QueryFirstOrDefaultAsync<ChapterFileRow>(
                "SELECT id AS Id, local_path AS LocalPath FROM chapter WHERE id = @Id LIMIT 1;",
                new { Id = id });
            if (row is null) return Results.NotFound(new { message = $"Chapter {id} não encontrado." });

            if (!string.IsNullOrEmpty(row.LocalPath) && File.Exists(row.LocalPath))
            {
                try { File.Delete(row.LocalPath); } catch { /* melhor esforço */ }
            }

            await conn.ExecuteAsync(
                @"UPDATE chapter
                  SET download_status = 'none',
                      local_path      = NULL,
                      file_size_bytes = NULL,
                      downloaded_at   = NULL
                  WHERE id = @Id;",
                new { Id = id });

            queue.Complete(id);
            return Results.NoContent();
        });

        return app;
    }

    private sealed record ChapterQueueRow(long Id, long PageCount, string DownloadStatus);
    private sealed record ChapterStatusRow(long Id, string DownloadStatus, string? LocalPath, long? FileSizeBytes, long PageCount);
    private sealed record ChapterFileRow(long Id, string? LocalPath);
}
