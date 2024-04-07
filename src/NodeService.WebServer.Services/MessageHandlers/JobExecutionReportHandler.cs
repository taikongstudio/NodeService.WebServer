using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NodeService.Infrastructure.Messages;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.MessageHandlers
{
    public class JobExecutionReportHandler : IMessageHandler
    {
        private readonly BatchQueue<JobExecutionReportMessage> _batchQueue;
        private readonly ILogger<HeartBeatResponseHandler> _logger;

        public JobExecutionReportHandler(
            BatchQueue<JobExecutionReportMessage> batchQueue,
            ILogger<HeartBeatResponseHandler> logger
            )
        {
            _batchQueue = batchQueue;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message)
        {
            this._batchQueue.Post(new JobExecutionReportMessage()
            {
                NodeSessionId = nodeSessionId,
                Message = message as JobExecutionReport
            });
            return ValueTask.CompletedTask;
        }
    }
}
