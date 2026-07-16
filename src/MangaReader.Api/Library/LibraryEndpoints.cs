using MangaReader.Api.MangaDex;

namespace MangaReader.Api.Library;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // Proxy de busca — não persiste.
        group.MapGet("/search", async (
            string title,
            MangaDexClient dex,
            MangaDexOptions options,
            CancellationToken cancellationToken,
            string? lang = null,
            int limit = 20) =>
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return Results.BadRequest(new { message = "Parâmetro 'title' é obrigatório." });
            }

            var results = await dex.SearchAsync(title, lang ?? "pt-br", limit, offset: 0, cancellationToken);
            var body = results.Select(m => new SearchResponseItem(
                m.Id,
                m.Title,
                m.Year,
                m.Status,
                m.CoverFilename is null
                    ? null
                    : $"{options.CoversBaseUrl.TrimEnd('/')}/{m.Id}/{m.CoverFilename}"));
            return Results.Ok(body);
        });

        group.MapGet("/manga", async (LibraryService library, CancellationToken cancellationToken) =>
            Results.Ok(await library.ListAsync(cancellationToken)));

        group.MapPost("/manga", async (
            AddMangaRequest request,
            LibraryService library,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.MangadexId))
            {
                return Results.BadRequest(new { message = "'mangadexId' é obrigatório." });
            }
            if (string.IsNullOrWhiteSpace(request.Language))
            {
                return Results.BadRequest(new { message = "'language' é obrigatório." });
            }

            try
            {
                var added = await library.AddAsync(request.MangadexId, request.Language, cancellationToken);
                return Results.Created($"/api/manga/{added.Id}", added);
            }
            catch (MangaAlreadyExistsException ex)
            {
                return Results.Conflict(new { message = ex.Message, id = ex.ExistingId });
            }
        });

        return app;
    }
}
