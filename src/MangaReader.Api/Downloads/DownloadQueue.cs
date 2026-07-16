using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MangaReader.Api.Downloads;

public sealed class DownloadQueue
{
    private readonly Channel<DownloadRequest> _channel =
        Channel.CreateUnbounded<DownloadRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ConcurrentDictionary<long, DownloadProgress> _progress = new();

    public bool Enqueue(long chapterId, DownloadQuality quality, int expectedPages)
    {
        var now = DateTimeOffset.UtcNow;
        _progress[chapterId] = new DownloadProgress(chapterId, DownloadState.Queued, 0, expectedPages, null, now);
        return _channel.Writer.TryWrite(new DownloadRequest(chapterId, quality));
    }

    internal IAsyncEnumerable<DownloadRequest> ConsumeAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkDownloading(long chapterId, int totalPages)
    {
        _progress.AddOrUpdate(
            chapterId,
            id => new DownloadProgress(id, DownloadState.Downloading, 0, totalPages, null, DateTimeOffset.UtcNow),
            (_, prev) => prev with
            {
                State = DownloadState.Downloading,
                Total = totalPages,
                Error = null,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
    }

    public void ReportPageDone(long chapterId, int pagesDone)
    {
        _progress.AddOrUpdate(
            chapterId,
            id => new DownloadProgress(id, DownloadState.Downloading, pagesDone, pagesDone, null, DateTimeOffset.UtcNow),
            (_, prev) => prev with { Done = pagesDone, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public void Complete(long chapterId) => _progress.TryRemove(chapterId, out _);

    public void Fail(long chapterId, string error)
    {
        _progress.AddOrUpdate(
            chapterId,
            id => new DownloadProgress(id, DownloadState.Failed, 0, 0, error, DateTimeOffset.UtcNow),
            (_, prev) => prev with { State = DownloadState.Failed, Error = error, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public DownloadProgress? TryGet(long chapterId)
        => _progress.TryGetValue(chapterId, out var p) ? p : null;

    public DownloadsSnapshotResponse Snapshot()
    {
        var all = _progress.Values.ToList();
        return new DownloadsSnapshotResponse(
            Queued: all.Where(p => p.State == DownloadState.Queued)
                       .OrderBy(p => p.UpdatedAt)
                       .ToList(),
            Downloading: all.Where(p => p.State == DownloadState.Downloading)
                            .OrderBy(p => p.UpdatedAt)
                            .ToList(),
            Failed: all.Where(p => p.State == DownloadState.Failed)
                       .OrderByDescending(p => p.UpdatedAt)
                       .ToList());
    }
}
