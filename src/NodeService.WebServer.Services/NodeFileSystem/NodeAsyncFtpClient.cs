using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeAsyncFtpClient : AsyncFtpClient
    {
        readonly FtpClientLease _ftpClientLease;

        public NodeAsyncFtpClient(
            FtpClientLease ftpClientLease,
            string host,
            string user,
            string pass,
            int port = 0,
            FtpConfig config = null,
            IFtpLogger logger = null) : base(host, user, pass, port, config, logger)
        {
            _ftpClientLease = ftpClientLease;
        }

        public override void Dispose()
        {
            this._ftpClientLease.Return(this);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            this._ftpClientLease.Return(this);
            return ValueTask.CompletedTask;
        }
    }
}
