using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Services.DataServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Counters
{
    public class WebServerCounterSnapshotService : BackgroundService
    {
        readonly ILogger<WebServerCounterSnapshotService> _logger;
        readonly WebServerCounter _webServerCounter;
        readonly ObjectCache _objectCache;
        readonly ExceptionCounter _exceptionCounter;

        public WebServerCounterSnapshotService(
            ILogger<WebServerCounterSnapshotService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            ObjectCache objectCache)
        {
            _logger = logger;
            _webServerCounter = webServerCounter;
            _objectCache = objectCache;
            _exceptionCounter = exceptionCounter;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _webServerCounter.SaveToCacheAsync(_objectCache, cancellationToken);
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
        }

    }
}
