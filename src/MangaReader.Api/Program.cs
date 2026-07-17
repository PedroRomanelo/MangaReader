using Dapper;
using MangaReader.Api.Downloads;
using MangaReader.Api.Infra;
using MangaReader.Api.Library;
using MangaReader.Api.MangaDex;
using MangaReader.Api.Sync;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var dbOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

if (string.IsNullOrWhiteSpace(dbOptions.ConnectionString))
{
    throw new InvalidOperationException(
        $"'{DatabaseOptions.SectionName}:ConnectionString' não configurada em appsettings.");
}

// Resolve Data Source relativo contra o ContentRoot do projeto (repo root em dev).
var csBuilder = new SqliteConnectionStringBuilder(dbOptions.ConnectionString);
if (!Path.IsPathRooted(csBuilder.DataSource))
{
    csBuilder.DataSource = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, csBuilder.DataSource));
}
dbOptions.ConnectionString = csBuilder.ToString();

builder.Services.AddSingleton(dbOptions);
builder.Services.AddSingleton<SqliteConnectionFactory>();

var mangaDexOptions = builder.Configuration
    .GetSection(MangaDexOptions.SectionName)
    .Get<MangaDexOptions>() ?? new MangaDexOptions();

if (string.IsNullOrWhiteSpace(mangaDexOptions.UserAgent))
{
    throw new InvalidOperationException(
        $"'{MangaDexOptions.SectionName}:UserAgent' não configurado. A MangaDex exige um User-Agent real.");
}

builder.Services.AddSingleton(mangaDexOptions);
builder.Services.AddSingleton<MangaDexRateLimiter>();
builder.Services.AddHttpClient<MangaDexClient>();
builder.Services.AddScoped<LibraryService>();

var downloadOptions = builder.Configuration
    .GetSection(DownloadOptions.SectionName)
    .Get<DownloadOptions>() ?? new DownloadOptions();

if (string.IsNullOrWhiteSpace(downloadOptions.LibraryRoot))
{
    throw new InvalidOperationException(
        $"'{DownloadOptions.SectionName}:LibraryRoot' não configurado em appsettings.");
}

if (!Path.IsPathRooted(downloadOptions.LibraryRoot))
{
    downloadOptions.LibraryRoot = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, downloadOptions.LibraryRoot));
}

builder.Services.AddSingleton(downloadOptions);
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddHttpClient<AtHomeReporter>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<DownloadWorker>();

builder.Services.AddScoped<SyncService>();

// LAN-only, usuário único: liberado. Quando entrar auth, restringe origens.
// Content-Range / Accept-Ranges precisam ser expostos pro fetch() do app
// enxergar o suporte a Range no stream do .cbz.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
        .WithExposedHeaders("Content-Length", "Content-Range", "Accept-Ranges"));
});

var app = builder.Build();

app.UseCors();

DatabaseBootstrapper.Initialize(app.Services);

// Se o processo caiu enquanto algo estava em 'downloading', a fila em memória
// perdeu o item. Marcamos como 'none' pra ficar disponível pra re-request.
using (var startupScope = app.Services.CreateScope())
{
    using var conn = startupScope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>().Create();
    var reset = conn.Execute("UPDATE chapter SET download_status = 'none' WHERE download_status = 'downloading';");
    if (reset > 0)
    {
        app.Logger.LogWarning("Reset de {Count} chapter(s) 'downloading' → 'none' no startup.", reset);
    }
}

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow.ToString("o"),
}));

app.MapLibraryEndpoints();
app.MapDownloadEndpoints();
app.MapSyncEndpoints();

app.Run();
