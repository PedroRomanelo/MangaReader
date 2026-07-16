using MangaReader.Api.Infra;
using MangaReader.Api.MangaDex;
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

var app = builder.Build();

DatabaseBootstrapper.Initialize(app.Services);

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow.ToString("o"),
}));

app.Run();
