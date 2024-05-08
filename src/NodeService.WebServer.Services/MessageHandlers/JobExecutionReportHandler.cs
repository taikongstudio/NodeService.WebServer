using Microsoft.AspNetCore.Http;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public class JobExecutionReportHandler : IMessageHandler
{
    readonly BatchQueue<JobExecutionReportMessage> _batchQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;

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
        _batchQueue.Post(new JobExecutionReportMessage
        {
            NodeSessionId = nodeSessionId,
            Message = message as JobExecutionReport
        });
        return ValueTask.CompletedTask;
    }
}