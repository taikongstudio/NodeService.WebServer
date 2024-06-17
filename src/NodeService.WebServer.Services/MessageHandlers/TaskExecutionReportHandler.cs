using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public class TaskExecutionReportHandler : IMessageHandler
{
    private readonly BatchQueue<TaskExecutionReportMessage> _batchQueue;
    private readonly ILogger<HeartBeatResponseHandler> _logger;

    public TaskExecutionReportHandler(
        BatchQueue<TaskExecutionReportMessage> batchQueue,
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
        _batchQueue.Post(new TaskExecutionReportMessage
        {
            NodeSessionId = nodeSessionId,
            Message = message as TaskExecutionReport
        });
        return ValueTask.CompletedTask;
    }
}