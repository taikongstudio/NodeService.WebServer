namespace NodeService.WebServer.Services.Counters;

public class WebServerCounter
{
    const string WebServerCounterSnapshot = nameof(WebServerCounterSnapshot);
    readonly ILogger<WebServerCounter> _logger;

    public WebServerCounter(ILogger<WebServerCounter> logger)
    {
        _logger = logger;
    }

    public WebServerCounterSnapshot Snapshot { get; set; } = new();

    public async ValueTask InitFromCacheAsync(
        ObjectCache objectCache,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await objectCache.GetObjectAsync<WebServerCounterSnapshot>(WebServerCounterSnapshot, cancellationToken);
            this.Snapshot = snapshot ?? new WebServerCounterSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }

    }

    public async ValueTask SaveToCacheAsync(
        ObjectCache objectCache,
        CancellationToken cancellationToken = default)
    {
        await objectCache.SetObjectAsync(WebServerCounterSnapshot, Snapshot, cancellationToken);
    }
}
