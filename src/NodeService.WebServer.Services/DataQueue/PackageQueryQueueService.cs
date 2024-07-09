using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.VirtualFileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.DataQueue
{
    public record struct PackageDownloadParameters
    {
        public PackageDownloadParameters(string packageId)
        {
            PackageId = packageId;
        }

        public string PackageId { get; init; }
    }

    public record struct PackageDownloadResult
    {
        public PackageDownloadResult(byte[]? contents)
        {
            Contents = contents;
        }

        public byte[]? Contents { get; init; }
    }

    public class PackageQueryQueueService : BackgroundService
    {
        readonly ILogger<PackageQueryQueueService> _logger;
        private readonly IServiceProvider _serviceProvider;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<BatchQueueOperation<PackageDownloadParameters, PackageDownloadResult>> _batchQueue;
        readonly ApplicationRepositoryFactory<PackageConfigModel> _packageRepoFactory;
        readonly IMemoryCache _memoryCache;

        public PackageQueryQueueService(
            ILogger<PackageQueryQueueService> logger,
            ExceptionCounter exceptionCounter,
            IMemoryCache memoryCache,
            IServiceProvider serviceProvider,
            BatchQueue<BatchQueueOperation<PackageDownloadParameters, PackageDownloadResult>> batchQueue,
            ApplicationRepositoryFactory<PackageConfigModel> packageRepoFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _exceptionCounter = exceptionCounter;
            _batchQueue = batchQueue;
            _packageRepoFactory = packageRepoFactory;
            _memoryCache = memoryCache;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var array in _batchQueue.ReceiveAllAsync(cancellationToken))
            {
                foreach (var packageQueryGroup in array.GroupBy(static x => x.Argument.PackageId))
                {
                    if (packageQueryGroup.Key == null)
                    {
                        continue;
                    }
                    await DownloadPackageAsync(packageQueryGroup);

                }
            }
        }

        async ValueTask DownloadPackageAsync(IGrouping<string, BatchQueueOperation<PackageDownloadParameters, PackageDownloadResult>> packageQueryGroup)
        {
            var packageId = packageQueryGroup.Key;
            try
            {

                var packageCacheKey = $"Package:{packageId}";
                var fileContents = await _memoryCache.GetOrCreateAsync(packageCacheKey, async () =>
                {
                    using var repo = _packageRepoFactory.CreateRepository();
                    var model = await repo.GetByIdAsync(packageId);
                    if (model == null || model.DownloadUrl == null)
                    {
                        foreach (var op in packageQueryGroup)
                        {
                            op.SetResult(new PackageDownloadResult(null));
                        }
                    }
                    using var stream = new MemoryStream();
                    using var scope = _serviceProvider.CreateScope();
                    using var virtualFileSystem = scope.ServiceProvider.GetService<IVirtualFileSystem>();
                    await virtualFileSystem.ConnectAsync();
                    if (await virtualFileSystem.DownloadStream(model.DownloadUrl, stream))
                    {
                        stream.Position = 0;
                        var hash = await CryptographyHelper.CalculateSHA256Async(stream);
                        stream.Position = 0;
                        if (hash != model.Hash) return null;
                        if (!ZipArchiveHelper.TryRead(stream, out var zipArchive)) return null;
                        if (!zipArchive.Entries.Any(ZipArchiveHelper.HasPackageKey)) return null;
                        stream.Position = 0;
                    }
                    return stream.ToArray();
                }, TimeSpan.FromHours(1));
                foreach (var op in packageQueryGroup)
                {
                    op.SetResult(new PackageDownloadResult(fileContents));
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex, packageId);
                _logger.LogError(ex.ToString());
            }

        }
    }
}
