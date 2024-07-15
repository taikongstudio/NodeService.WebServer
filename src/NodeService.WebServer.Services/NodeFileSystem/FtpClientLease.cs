using System.Threading;

namespace NodeService.WebServer.Services.NodeFileSystem
{
   public  class FtpClientLease
    {
        readonly FtpConfiguration _ftpConfiguration;
        readonly SemaphoreSlim _semaphoreSlim;
        readonly ConcurrentQueue<NodeAsyncFtpClient> _queue;
        readonly Timer _timer;
        readonly BatchBlock<NodeAsyncFtpClient> _batchBlock;

        public FtpClientLease(FtpConfiguration ftpConfiguration, int maxClientPerConfiguration)
        {
            _ftpConfiguration = ftpConfiguration;
            _semaphoreSlim = new SemaphoreSlim(maxClientPerConfiguration);
            _queue = new ConcurrentQueue<NodeAsyncFtpClient>();

        }

        public async ValueTask TestConnectionAsync()
        {
            foreach (var item in _queue)
            {
                if (!item.IsConnected)
                {
                    await item.AutoConnect();
                }
                else
                {
                    var workingDirectory = await item.GetWorkingDirectory();
                    await item.GetModifiedTime(workingDirectory);
                }
            }
        }


        public async ValueTask<AsyncFtpClient> CreateClientAsync(TimeSpan timeOut, CancellationToken cancellationToken = default)
        {
            if (!await _semaphoreSlim.WaitAsync(timeOut, cancellationToken))
            {
                return CreateNonPooledFtpClient(_ftpConfiguration);
            }

            if (!this._queue.TryDequeue(out NodeAsyncFtpClient? nodeAsyncFtpClient))
            {
                nodeAsyncFtpClient = CreatePooledFtpClient(_ftpConfiguration);
                if (!nodeAsyncFtpClient.IsConnected)
                {
                    await nodeAsyncFtpClient.AutoConnect(cancellationToken);
                }
            }
            return nodeAsyncFtpClient;
        }

        NodeAsyncFtpClient CreatePooledFtpClient(FtpConfiguration ftpConfig)
        {
            var asyncFtpClient = new NodeAsyncFtpClient(this,ftpConfig.Host, ftpConfig.Username, ftpConfig.Password, ftpConfig.Port,
                                new FtpConfig()
                                {
                                    ConnectTimeout = ftpConfig.ConnectTimeout,
                                    ReadTimeout = ftpConfig.ReadTimeout,
                                    SslProtocols = System.Security.Authentication.SslProtocols.None,
                                    DataConnectionReadTimeout = ftpConfig.DataConnectionReadTimeout,
                                    DataConnectionConnectTimeout = ftpConfig.DataConnectionConnectTimeout,
                                    DataConnectionType = (FtpDataConnectionType)ftpConfig.DataConnectionType
                                });
            return asyncFtpClient;
        }

        AsyncFtpClient CreateNonPooledFtpClient(FtpConfiguration ftpConfig)
        {
            var asyncFtpClient = new AsyncFtpClient(ftpConfig.Host, ftpConfig.Username, ftpConfig.Password, ftpConfig.Port,
                                new FtpConfig()
                                {
                                    ConnectTimeout = ftpConfig.ConnectTimeout,
                                    ReadTimeout = ftpConfig.ReadTimeout,
                                    SslProtocols = System.Security.Authentication.SslProtocols.None,
                                    DataConnectionReadTimeout = ftpConfig.DataConnectionReadTimeout,
                                    DataConnectionConnectTimeout = ftpConfig.DataConnectionConnectTimeout,
                                    DataConnectionType = (FtpDataConnectionType)ftpConfig.DataConnectionType
                                });
            return asyncFtpClient;
        }

        public void Return(NodeAsyncFtpClient nodeAsyncFtpClient)
        {
            this._queue.Enqueue(nodeAsyncFtpClient);
            _semaphoreSlim.Release();
        }


    }
}
