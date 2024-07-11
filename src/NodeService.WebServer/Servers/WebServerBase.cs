using System.Text;

namespace NodeService.WebServer.Servers
{
    public abstract class WebServerBase
    {
        public abstract Task RunAsync(CancellationToken cancellationToken = default);
    }
}