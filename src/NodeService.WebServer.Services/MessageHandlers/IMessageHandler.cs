using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public interface IMessageHandler : IAsyncDisposable
{
    HttpContext HttpContext { get; set; }
    ValueTask HandleAsync(NodeSessionId nodeSessionId, IMessage message, CancellationToken cancellationToken);
}