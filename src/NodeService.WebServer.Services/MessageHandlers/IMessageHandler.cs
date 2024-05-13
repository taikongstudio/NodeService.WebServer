using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public interface IMessageHandler : IAsyncDisposable
{
    ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message);
}