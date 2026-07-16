namespace MangaReader.Api.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sync/manifest", async (SyncService sync, CancellationToken cancellationToken) =>
            Results.Ok(await sync.GetManifestAsync(cancellationToken)));

        app.MapGet("/api/sync/progress", async (SyncService sync, CancellationToken cancellationToken) =>
            Results.Ok(await sync.GetProgressAsync(cancellationToken)));

        app.MapPost("/api/sync/progress", async (
            List<SyncProgressItem>? items,
            SyncService sync,
            CancellationToken cancellationToken) =>
        {
            if (items is null || items.Count == 0)
            {
                return Results.Ok(new SyncMergeResponse(0, 0));
            }

            var merged = await sync.MergeProgressAsync(items, cancellationToken);
            return Results.Ok(new SyncMergeResponse(items.Count, merged));
        });

        return app;
    }
}
