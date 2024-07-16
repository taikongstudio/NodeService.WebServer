using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Services.NodeSessions.MessageHandlers;

public interface IMessageHandler : IAsyncDisposable
{
    HttpContext HttpContext { get; set; }

    NodeSessionId NodeSessionId { get; }

    ValueTask InitAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default);

    ValueTask HandleAsync(IMessage message, CancellationToken cancellationToken = default);

}