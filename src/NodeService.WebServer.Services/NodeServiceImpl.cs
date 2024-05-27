using Azure;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.MessageHandlers;
using static NodeService.Infrastructure.Services.NodeService;


namespace NodeService.WebServer.Services;

public class NodeServiceImpl : NodeServiceBase
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<NodeServiceImpl> _logger;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    private readonly ApplicationRepositoryFactory<NodeProfileModel> _nodeProfileRepositoryFactory;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IOptionsMonitor<WebServerOptions> _optionMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly WebServerCounter _webServerCounter;

    public NodeServiceImpl(
        ILogger<NodeServiceImpl> logger,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<NodeProfileModel> nodeProfileRepositoryFactory,
        IServiceProvider serviceProvider,
        INodeSessionService nodeService,
        IOptionsMonitor<WebServerOptions> optionsMonitor,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter
    )
    {
        _logger = logger;
        _optionMonitor = optionsMonitor;
        _serviceProvider = serviceProvider;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _nodeProfileRepositoryFactory = nodeProfileRepositoryFactory;
        _nodeSessionService = nodeService;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
    }


    public async Task<NodeId> EnsureNodeInfoAsync(
        NodeSessionId nodeSessionId,
        string nodeName,
        CancellationToken cancellationToken = default)
    {
        using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
        using var nodeProfileRepo = _nodeProfileRepositoryFactory.CreateRepository();
        var nodeId = nodeSessionId.NodeId.Value;
        var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);

        if (nodeInfo == null)
        {
            nodeInfo = NodeInfoModel.Create(nodeId, nodeName);
            var nodeProfile = await nodeProfileRepo.FirstOrDefaultAsync(new NodeProfileSpecification(nodeName), cancellationToken);
            if (nodeProfile != null)
            {
                var oldNodeInfo = await nodeInfoRepo.GetByIdAsync(nodeProfile.NodeInfoId, cancellationToken);
                if (oldNodeInfo != null) await nodeInfoRepo.DeleteAsync(oldNodeInfo, cancellationToken);
                nodeProfile.NodeInfoId = nodeId;
                nodeInfo.ProfileId = nodeProfile.Id;
            }
            nodeInfo.Status = NodeStatus.Online;
            await nodeInfoRepo.AddAsync(nodeInfo, cancellationToken);
        }

        return new NodeId(nodeInfo.Id);
    }


    public override async Task Subscribe(
        SubscribeRequest subscribeRequest,
        IServerStreamWriter<SubscribeEvent> responseStream,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        try
        {
            if (nodeSessionId.NodeId.IsNullOrEmpty) return;
            var messageHandlerDictionary = _serviceProvider.GetService<MessageHandlerDictionary>();
            await EnsureNodeInfoAsync(nodeSessionId, nodeClientHeaders.HostName);
            _nodeSessionService.UpdateNodeStatus(nodeSessionId, NodeStatus.Online);
            _nodeSessionService.UpdateNodeName(nodeSessionId, nodeClientHeaders.HostName);
            _nodeSessionService.SetHttpContext(nodeSessionId, httpContext);
            var inputQueue = _nodeSessionService.GetInputQueue(nodeSessionId);
            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var message in inputQueue.ReadAllAsync(context.CancellationToken))
                    {
                        _webServerCounter.NodeServiceInputMessagesCount++;
                        if (!messageHandlerDictionary.TryGetValue(message.Descriptor, out var messageHandler)) continue;
                        await messageHandler.HandleAsync(nodeSessionId, httpContext, message);
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
            }, context.CancellationToken);

            await DispatchSubscribeEvents(nodeSessionId, responseStream, context);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task DispatchSubscribeEvents(
        NodeSessionId nodeContextId,
        IServerStreamWriter<SubscribeEvent> responseStream,
        ServerCallContext context)
    {
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var outputQueue = _nodeSessionService.GetOutputQueue(nodeContextId);

        await foreach (var message in outputQueue.ReadAllAsync(context.CancellationToken))
        {
            if (message is not INodeMessage nodeMessage || nodeMessage.IsExpired)
            {
                _webServerCounter.NodeServiceExpiredMessagesCount++;
                continue;
            }

            if (message.Descriptor == SubscribeEvent.Descriptor)
            {
                if (message is not SubscribeEvent subscribeEvent) continue;
                subscribeEvent.Properties.TryAdd("DateTime",
                    nodeMessage.CreatedDateTime.ToString(NodePropertyModel.DateTimeFormatString));
                await responseStream.WriteAsync(subscribeEvent);
                _webServerCounter.NodeServiceOutputMessagesCount++;
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

    public override async Task<Empty> SendFileSystemBulkOperationReport(FileSystemBulkOperationReport request, ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.GetInputQueue(nodeSessionId).EnqueueAsync(request, context.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> SendFileSystemBulkOperationResponse(
        FileSystemBulkOperationResponse response,
        ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
        return new Empty();
    }

    public override async Task<Empty> SendJobExecutionEventResponse(
        JobExecutionEventResponse response,
        ServerCallContext context)
    {
        var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
        var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
        await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
        return new Empty();
    }


    public override async Task<Empty> SendJobExecutionReport(
        IAsyncStreamReader<JobExecutionReport> requestStream,
        ServerCallContext context)
    {
        try
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            await foreach (var report in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
                await _nodeSessionService.GetInputQueue(nodeSessionId).EnqueueAsync(report, context.CancellationToken);
            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return new Empty();
    }
}