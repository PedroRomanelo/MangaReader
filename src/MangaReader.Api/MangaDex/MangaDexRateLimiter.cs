using System.Threading.RateLimiting;

namespace MangaReader.Api.MangaDex;

// Dois limites impostos pela MangaDex:
//   - api.mangadex.org: 5 req/s global
//   - /at-home/server: 40 req/min
// Usamos SlidingWindow para as duas janelas; chamadas at-home consomem os dois.
public sealed class MangaDexRateLimiter : IAsyncDisposable
{
    private readonly SlidingWindowRateLimiter _global;
    private readonly SlidingWindowRateLimiter _atHome;

    public MangaDexRateLimiter()
    {
        _global = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromSeconds(1),
            SegmentsPerWindow = 10,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
        _atHome = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 40,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 60,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public async ValueTask AcquireAsync(bool atHome, CancellationToken cancellationToken)
    {
        using var globalLease = await _global.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!globalLease.IsAcquired)
        {
            throw new InvalidOperationException("Não foi possível adquirir permit global da MangaDex.");
        }

        if (!atHome) return;

        using var atHomeLease = await _atHome.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!atHomeLease.IsAcquired)
        {
            throw new InvalidOperationException("Não foi possível adquirir permit at-home da MangaDex.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _global.DisposeAsync().ConfigureAwait(false);
        await _atHome.DisposeAsync().ConfigureAwait(false);
    }
}
