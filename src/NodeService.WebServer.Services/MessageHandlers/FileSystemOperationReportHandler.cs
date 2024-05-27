using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.MessageHandlers
{
    public class FileSystemOperationReportHandler : IMessageHandler
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message)
        {
            return ValueTask.CompletedTask;
        }
    }
}
