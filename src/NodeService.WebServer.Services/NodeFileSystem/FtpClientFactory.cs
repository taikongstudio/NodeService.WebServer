using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using System.Threading;

namespace NodeService.WebServer.Services.NodeFileSystem
{

    public class FtpClientFactory
    {
        readonly int _maxClientPerConfiguration;

        readonly ConcurrentDictionary<FtpConfiguration, FtpClientLease> _asyncFtpClientDict;
        private readonly ActionBlock<int> _actionBlock;
        private readonly Timer _timer;

        public FtpClientFactory(int maxClientPerConfiguration = 8)
        {
            _maxClientPerConfiguration = maxClientPerConfiguration;
            _asyncFtpClientDict = new ConcurrentDictionary<FtpConfiguration, FtpClientLease>();
            _actionBlock = new ActionBlock<int>(TestConnectionAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
            _timer = new Timer(GenerateSingal, null, 500, 3000);
           
        }

        void GenerateSingal(object? state)
        {
            _actionBlock.Post(Random.Shared.Next());
        }


        async Task TestConnectionAsync(int number)
        {
            foreach (var item in _asyncFtpClientDict.Values)
            {
                await item.TestConnectionAsync();
            }
        }

        public async ValueTask<AsyncFtpClient> CreateClientAsync(FtpConfiguration ftpConfiguration, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var lease = _asyncFtpClientDict.GetOrAdd(ftpConfiguration, new FtpClientLease(ftpConfiguration, _maxClientPerConfiguration));
            return await lease.CreateClientAsync(timeout, cancellationToken);
        }



    }
}
