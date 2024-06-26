using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public class TaskExecutionReportHandler : IMessageHandler
{
    readonly BatchQueue<TaskExecutionReportMessage> _batchQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportHandler(
        BatchQueue<TaskExecutionReportMessage> batchQueue,
        ILogger<HeartBeatResponseHandler> logger,
        WebServerCounter webServerCounter
    )
    {
        _batchQueue = batchQueue;
        _logger = logger;
        _webServerCounter = webServerCounter;
    }

    public HttpContext HttpContext { get; set; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
    public async ValueTask HandleAsync(NodeSessionId nodeSessionId, IMessage message, CancellationToken cancellationToken)
    {
        _webServerCounter.TaskExecutionReportRecieveCount.Value++;
        await _batchQueue.SendAsync(new TaskExecutionReportMessage
        {
            NodeSessionId = nodeSessionId,
            Message = message as TaskExecutionReport
        }, cancellationToken);
    }
}