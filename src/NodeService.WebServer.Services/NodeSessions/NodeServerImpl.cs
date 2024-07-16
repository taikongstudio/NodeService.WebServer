using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions.MessageHandlers;
using static NodeService.Infrastructure.Services.NodeService;


namespace NodeService.WebServer.Services.NodeSessions;

public class NodeServerImpl : NodeServiceBase
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<NodeServerImpl> _logger;
    readonly IAsyncQueue<ConfigurationChangedEvent> _notificationConfigChangedQueue;
    readonly NodeInfoQueryService _nodeQueryService;
    readonly INodeSessionService _nodeSessionService;
    readonly IOptionsMonitor<WebServerOptions> _optionMonitor;
    readonly IServiceProvider _serviceProvider;
    readonly WebServerCounter _webServerCounter;

    public NodeServerImpl(
        ILogger<NodeServerImpl> logger,
        NodeInfoQueryService nodeQueryService,
        ApplicationRepositoryFactory<FileSystemWatchConfigModel> fileSystemWatchConfigRepoFactory,
        IServiceProvider serviceProvider,
        INodeSessionService nodeService,
        IOptionsMonitor<WebServerOptions> optionsMonitor,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        IAsyncQueue<ConfigurationChangedEvent> notificationConfigChangedQueue
    )
    {
        _logger = logger;
        _optionMonitor = optionsMonitor;
        _serviceProvider = serviceProvider;
        _nodeSessionService = nodeService;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
        _notificationConfigChangedQueue = notificationConfigChangedQueue;
        _nodeQueryService = nodeQueryService;
    }





    public override async Task Subscribe(
        SubscribeRequest subscribeRequest,
        IServerStreamWriter<SubscribeEvent> responseStream,
        ServerCallContext context)
    {

        try
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            if (nodeSessionId.NodeId.IsNullOrEmpty) return;

            await _nodeQueryService.EnsureNodeInfoAsync(nodeSessionId.NodeId.Value, nodeClientHeaders.HostName);
            _nodeSessionService.UpdateNodeStatus(nodeSessionId, NodeStatus.Online);
            _nodeSessionService.UpdateNodeName(nodeSessionId, nodeClientHeaders.HostName);
            _nodeSessionService.SetHttpContext(nodeSessionId, httpContext);

            Task[] tasks =
                [
                    ProcessOutputQueueAsync(nodeSessionId, context.CancellationToken),
                    ProcessInputQueueAsync(nodeSessionId, context.CancellationToken),
                ];

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        async Task ProcessInputQueueAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                var inputQueue = _nodeSessionService.GetInputQueue(nodeSessionId);
                var httpContext = _nodeSessionService.GetHttpContext(nodeSessionId);
                var messageHandlerDictionary = _serviceProvider.GetService<MessageHandlerDictionary>();
                foreach (var item in messageHandlerDictionary)
                {
                    item.Value.HttpContext = httpContext;
                    await item.Value.InitAsync(nodeSessionId, cancellationToken);
                }
                await foreach (var message in inputQueue.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        _webServerCounter.NodeServiceInputMessagesCount.Value++;
                        if (!messageHandlerDictionary.TryGetValue(message.Descriptor, out var messageHandler)) continue;
                        await messageHandler.HandleAsync(message, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex, nodeSessionId.NodeId.Value);
                    }

                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex, nodeSessionId.NodeId.Value);
                _logger.LogError(ex.ToString());
            }
        }

        async Task ProcessOutputQueueAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default)
        {
            try
            {
                var outputQueue = _nodeSessionService.GetOutputQueue(nodeSessionId);

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (await outputQueue.WaitToReadAsync(cancellationToken))
                    {
                        if (!outputQueue.TryPeek(out IMessage message))
                        {
                            continue;
                        }
                        if (message is not INodeMessage nodeMessage || nodeMessage.IsExpired)
                        {
                            _webServerCounter.NodeServiceExpiredMessagesCount.Value++;
                            await outputQueue.DeuqueAsync(cancellationToken);
                            continue;
                        }

                        if (message.Descriptor == SubscribeEvent.Descriptor)
                        {
                            if (message is not SubscribeEvent subscribeEvent)
                            {
                                await outputQueue.DeuqueAsync(cancellationToken);
                                continue;
                            }
                            subscribeEvent.Properties.TryAdd("DateTime", nodeMessage.CreatedDateTime.ToString(NodePropertyModel.DateTimeFormatString));
                            await responseStream.WriteAsync(subscribeEvent, cancellationToken);
                            _webServerCounter.NodeServiceOutputMessagesCount.Value++;
                        }
                        await outputQueue.DeuqueAsync(cancellationToken);
                    }
                }

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex, nodeSessionId.NodeId.Value);
                _logger.LogError(ex.ToString());
            }


        }
  }


    public override async Task<Empty> SendFileSystemListDirectoryResponse(
        FileSystemListDirectoryResponse response,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
        return new Empty();
    }

    public override async Task<Empty> SendFileSystemListDriveResponse(
        FileSystemListDriveResponse response,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
        return new Empty();
    }

    public override async Task<Empty> SendHeartBeatResponse(
        HeartBeatResponse response,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteHeartBeatResponseAsync(nodeSessionId, response);
        return new Empty();
    }

    public override async Task<Empty> SendTaskExecutionEventResponse(
        TaskExecutionEventResponse response,
        ServerCallContext context)
    {
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
        return new Empty();
    }


    public override async Task<Empty> SendTaskExecutionReport(
        IAsyncStreamReader<TaskExecutionReport> requestStream,
        ServerCallContext context)
    {
        NodeSessionId nodeSessionId = default;
        try
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            await foreach (var report in requestStream.ReadAllAsync(context.CancellationToken))
            {
                nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                await _nodeSessionService.GetInputQueue(nodeSessionId).EnqueueAsync(report, context.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, nodeSessionId.NodeId.Value);
            _logger.LogError(ex.ToString());
        }

        return new Empty();
    }

    public override async Task<Empty> SendFileSystemWatchEventReport(
        IAsyncStreamReader<FileSystemWatchEventReport> requestStream, ServerCallContext context)
    {
        NodeSessionId nodeSessionId = default;
        try
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            await foreach (var report in requestStream.ReadAllAsync(context.CancellationToken))
            {

            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, nodeSessionId.NodeId.Value);
            _logger.LogError(ex.ToString());
        }

        return new Empty();
    }
}