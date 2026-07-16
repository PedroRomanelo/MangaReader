using System.Net.Http.Json;
using MangaReader.Api.MangaDex;

namespace MangaReader.Api.Downloads;

// Fire-and-forget POST em api.mangadex.network/report.
// Regra do AUP (docs/ARQUITETURA.md §4): para cada imagem baixada de um nó
// @Home (host != mangadex.org), reportar o resultado.
public sealed class AtHomeReporter
{
    private readonly HttpClient _http;
    private readonly MangaDexOptions _options;
    private readonly ILogger<AtHomeReporter> _logger;

    public AtHomeReporter(HttpClient http, MangaDexOptions options, ILogger<AtHomeReporter> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    public void FireReport(string url, bool success, long bytes, long durationMs, bool cached)
    {
        // Só reporta se a base NÃO for mangadex.org — mangadex.org é
        // servido direto pela MangaDex, sem participação da rede @Home.
        Uri uri;
        try { uri = new Uri(url); }
        catch { return; }
        if (uri.Host.EndsWith("mangadex.org", StringComparison.OrdinalIgnoreCase)) return;

        var payload = new
        {
            url,
            success,
            bytes,
            duration = durationMs,
            cached,
        };

        _ = Task.Run(async () =>
        {
            try
            {
                using var content = JsonContent.Create(payload);
                using var resp = await _http.PostAsync(_options.ReportUrl, content);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Report @Home retornou {Status}", (int)resp.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Falha ao enviar report @Home (fire-and-forget).");
            }
        });
    }
}
