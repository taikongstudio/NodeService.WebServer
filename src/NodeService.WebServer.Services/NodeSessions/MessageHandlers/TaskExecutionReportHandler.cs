using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.NodeSessions.MessageHandlers;

public class TaskExecutionReportHandler : IMessageHandler
{
    readonly IAsyncQueue<TaskExecutionReport> _reportQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportHandler(
        IAsyncQueue<TaskExecutionReport> batchQueue,
        ILogger<HeartBeatResponseHandler> logger,
        WebServerCounter webServerCounter
    )
    {
        _reportQueue = batchQueue;
        _logger = logger;
        _webServerCounter = webServerCounter;
    }

    public HttpContext HttpContext { get; set; }

    public NodeSessionId NodeSessionId { get; private set; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        _webServerCounter.TaskExecutionReportRecieveCount.Value++;
        await _reportQueue.EnqueueAsync(message as TaskExecutionReport, cancellationToken);
    }

    public ValueTask InitAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default)
    {
        NodeSessionId = nodeSessionId;
        return ValueTask.CompletedTask;
    }
}