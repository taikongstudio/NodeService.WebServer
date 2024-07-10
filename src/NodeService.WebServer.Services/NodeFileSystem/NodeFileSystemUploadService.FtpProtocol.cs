using FluentFTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public partial class NodeFileSystemUploadService
    {
        readonly List<FtpClientProcessContext> _asyncFtpClients;

        AsyncFtpClient CreateFtpClient(FtpConfigModel ftpConfig)
        {
            var asyncFtpClient = new AsyncFtpClient(ftpConfig.Host, ftpConfig.Username, ftpConfig.Password, ftpConfig.Port,
                                new FtpConfig()
                                {
                                    ConnectTimeout = ftpConfig.ConnectTimeout,
                                    ReadTimeout = ftpConfig.ReadTimeout,
                                    DataConnectionReadTimeout = ftpConfig.DataConnectionReadTimeout,
                                    DataConnectionConnectTimeout = ftpConfig.DataConnectionConnectTimeout,
                                    DataConnectionType = (FtpDataConnectionType)ftpConfig.DataConnectionType
                                });
            return asyncFtpClient;
        }

        void ReturnFtpClient(AsyncFtpClient asyncFtpClient)
        {

        }
    }
}
