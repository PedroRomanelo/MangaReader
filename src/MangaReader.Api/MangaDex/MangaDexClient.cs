using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MangaReader.Api.MangaDex.Internal;

namespace MangaReader.Api.MangaDex;

public sealed class MangaDexClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MangaDexRateLimiter _rateLimiter;
    private readonly ILogger<MangaDexClient> _logger;

    public MangaDexClient(
        HttpClient http,
        MangaDexOptions options,
        MangaDexRateLimiter rateLimiter,
        ILogger<MangaDexClient> logger)
    {
        _http = http;
        _rateLimiter = rateLimiter;
        _logger = logger;

        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MangaSearchResult>> SearchAsync(
        string title,
        string? language = null,
        int limit = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var qs = new QueryStringBuilder()
            .Add("title", title)
            .Add("limit", limit.ToString())
            .Add("offset", offset.ToString())
            .Add("includes[]", "cover_art")
            .AddContentRatings()
            .AddIfNotEmpty("availableTranslatedLanguage[]", language);

        var envelope = await GetJsonAsync<ListEnvelope<MangaResource>>(
            $"manga{qs}", atHome: false, cancellationToken).ConfigureAwait(false);

        var results = new List<MangaSearchResult>(envelope.Data.Length);
        foreach (var manga in envelope.Data)
        {
            results.Add(ToSearchResult(manga, language));
        }
        return results;
    }

    public async Task<IReadOnlyList<ChapterFeedItem>> GetFeedAsync(
        string mangaId,
        string? language = null,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var qs = new QueryStringBuilder()
            .Add("limit", limit.ToString())
            .Add("offset", offset.ToString())
            .Add("includes[]", "scanlation_group")
            .Add("order[chapter]", "asc")
            .AddContentRatings()
            .AddIfNotEmpty("translatedLanguage[]", language);

        var envelope = await GetJsonAsync<ListEnvelope<ChapterResource>>(
            $"manga/{Uri.EscapeDataString(mangaId)}/feed{qs}", atHome: false, cancellationToken)
            .ConfigureAwait(false);

        var chapters = new List<ChapterFeedItem>(envelope.Data.Length);
        foreach (var chapter in envelope.Data)
        {
            chapters.Add(ToFeedItem(chapter));
        }
        return chapters;
    }

    public async Task<MangaSearchResult> GetMangaAsync(
        string mangadexId,
        string? preferredLanguage = null,
        CancellationToken cancellationToken = default)
    {
        var qs = new QueryStringBuilder().Add("includes[]", "cover_art");
        var envelope = await GetJsonAsync<EntityEnvelope<MangaResource>>(
            $"manga/{Uri.EscapeDataString(mangadexId)}{qs}", atHome: false, cancellationToken)
            .ConfigureAwait(false);

        if (envelope.Data is null)
        {
            throw new InvalidOperationException($"Manga {mangadexId} não encontrado na MangaDex.");
        }
        return ToSearchResult(envelope.Data, preferredLanguage);
    }

    public async Task<AtHomeServer> GetAtHomeServerAsync(string chapterId, CancellationToken cancellationToken = default)
    {
        var response = await GetJsonAsync<AtHomeServerResponse>(
            $"at-home/server/{Uri.EscapeDataString(chapterId)}", atHome: true, cancellationToken)
            .ConfigureAwait(false);

        if (response.Chapter is null)
        {
            throw new InvalidOperationException("Resposta at-home/server sem bloco 'chapter'.");
        }

        return new AtHomeServer(
            response.BaseUrl,
            response.Chapter.Hash,
            response.Chapter.Data,
            response.Chapter.DataSaver);
    }

    private static MangaSearchResult ToSearchResult(MangaResource manga, string? preferredLanguage)
    {
        var title = PickTitle(manga.Attributes?.Title, preferredLanguage);
        var cover = manga.Relationships
            .FirstOrDefault(r => r.Type == "cover_art")?
            .Attributes?
            .FileName;
        return new MangaSearchResult(
            manga.Id,
            title,
            manga.Attributes?.Year,
            manga.Attributes?.Status,
            manga.Attributes?.ContentRating,
            cover);
    }

    private static ChapterFeedItem ToFeedItem(ChapterResource chapter)
    {
        var group = chapter.Relationships
            .FirstOrDefault(r => r.Type == "scanlation_group")?
            .Attributes?
            .Name;
        var attrs = chapter.Attributes;
        return new ChapterFeedItem(
            chapter.Id,
            attrs?.Volume,
            attrs?.Chapter,
            attrs?.Title,
            attrs?.TranslatedLanguage ?? "",
            group,
            attrs?.Pages ?? 0,
            attrs?.PublishAt);
    }

    private static string PickTitle(Dictionary<string, string>? titles, string? preferredLanguage)
    {
        if (titles is null || titles.Count == 0) return "";
        if (!string.IsNullOrEmpty(preferredLanguage)
            && titles.TryGetValue(preferredLanguage, out var preferred))
        {
            return preferred;
        }
        return titles.TryGetValue("en", out var en) ? en : titles.Values.First();
    }

    private async Task<T> GetJsonAsync<T>(string relativeUrl, bool atHome, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, relativeUrl), atHome, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MangaDexApiException((int)response.StatusCode, body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken).ConfigureAwait(false);
        return parsed ?? throw new InvalidOperationException($"JSON vazio em {relativeUrl}");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory, bool atHome, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        HttpResponseMessage? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            last?.Dispose();
            await _rateLimiter.AcquireAsync(atHome, cancellationToken).ConfigureAwait(false);

            using var request = requestFactory();
            last = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var isTransient = last.StatusCode == HttpStatusCode.TooManyRequests || (int)last.StatusCode >= 500;
            if (!isTransient || attempt == maxAttempts)
            {
                return last;
            }

            var delay = last.StatusCode == HttpStatusCode.TooManyRequests
                        && last.Headers.RetryAfter?.Delta is TimeSpan retryAfter
                ? retryAfter
                : TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

            _logger.LogWarning(
                "MangaDex {Status} — retry {Next}/{Max} em {DelayMs}ms",
                (int)last.StatusCode, attempt + 1, maxAttempts, delay.TotalMilliseconds);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        return last!;
    }
}

public sealed class MangaDexApiException : Exception
{
    public int StatusCode { get; }
    public string? Body { get; }

    public MangaDexApiException(int statusCode, string? body)
        : base($"MangaDex retornou {statusCode}. Body: {body}")
    {
        StatusCode = statusCode;
        Body = body;
    }
}

internal sealed class QueryStringBuilder
{
    private readonly StringBuilder _sb = new();

    public QueryStringBuilder Add(string key, string value)
    {
        _sb.Append(_sb.Length == 0 ? '?' : '&');
        _sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        return this;
    }

    public QueryStringBuilder AddIfNotEmpty(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Add(key, value);
        return this;
    }

    // Uso pessoal — inclui todos os ratings; filtro fica com o consumidor.
    public QueryStringBuilder AddContentRatings()
    {
        Add("contentRating[]", "safe");
        Add("contentRating[]", "suggestive");
        Add("contentRating[]", "erotica");
        Add("contentRating[]", "pornographic");
        return this;
    }

    public override string ToString() => _sb.ToString();
}
