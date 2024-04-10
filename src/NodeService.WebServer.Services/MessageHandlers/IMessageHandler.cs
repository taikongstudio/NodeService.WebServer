using Microsoft.AspNetCore.Http;

namespace NodeService.WebServer.Services.MessageHandlers
{
    public interface IMessageHandler : IAsyncDisposable
    {
        ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message);

    }
}
