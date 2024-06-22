using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.MessageHandlers;

public class FileSystemOperationReportHandler : IMessageHandler
{
    public HttpContext HttpContext { get; set; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(NodeSessionId nodeSessionId, IMessage message, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}