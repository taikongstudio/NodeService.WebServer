using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.MessageHandlers
{
    public interface IMessageHandler : IAsyncDisposable
    {
        ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message);

    }
}
