using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.MessageHandlers;
using static NodeService.Infrastructure.Services.NodeService;


namespace NodeService.WebServer.Services
{
    public partial class NodeServiceImpl : NodeServiceBase
    {
        private readonly ILogger<NodeServiceImpl> _logger;
        private readonly IOptionsMonitor<WebServerOptions> _optionMonitor;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly INodeSessionService _nodeSessionService;


        public NodeServiceImpl(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IServiceProvider serviceProvider,
            INodeSessionService nodeService,
            ILogger<NodeServiceImpl> logger,
            IOptionsMonitor<WebServerOptions> optionsMonitor
            )
        {
            _optionMonitor = optionsMonitor;
            _serviceProvider = serviceProvider;
            _dbContextFactory = dbContextFactory;
            _nodeSessionService = nodeService;
            _logger = logger;
        }

        public override async Task Subscribe(SubscribeRequest subscribeRequest, IServerStreamWriter<SubscribeEvent> responseStream, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            try
            {
                if (nodeSessionId.NodeId.IsNullOrEmpty)
                {
                    return;
                }
                var messageHandlerDictionary = this._serviceProvider.GetService<MessageHandlerDictionary>();
                await _nodeSessionService.EnsureNodeInfoAsync(nodeSessionId, nodeClientHeaders.HostName);
                _nodeSessionService.UpdateNodeStatus(nodeSessionId, NodeStatus.Online);
                _nodeSessionService.UpdateNodeName(nodeSessionId, nodeClientHeaders.HostName);
                _nodeSessionService.SetHttpContext(nodeSessionId, httpContext);
                var inputQueue = _nodeSessionService.GetInputQueue(nodeSessionId);
                _ = Task.Run(async () =>
                 {
                     try
                     {
                         await foreach (var message in inputQueue.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                         {
                             if (!messageHandlerDictionary.TryGetValue(message.Descriptor, out var messageHandler))
                             {
                                 continue;
                             }
                             await messageHandler.HandleAsync(nodeSessionId, httpContext, message);
                         }
                     }
                     catch (Exception ex)
                     {
                         _logger.LogError(ex.ToString());
                     }

                 }, context.CancellationToken);

                await DispatchSubscribeEvents(nodeSessionId, responseStream, context);


            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _nodeSessionService.UpdateNodeStatus(nodeSessionId, NodeStatus.Offline);
            }

        }

        private async Task DispatchSubscribeEvents(NodeSessionId nodeContextId, IServerStreamWriter<SubscribeEvent> responseStream, ServerCallContext context)
        {
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            var outputQueue = _nodeSessionService.GetOutputQueue(nodeContextId);

            await foreach (var message in outputQueue.ReadAllAsync(context.CancellationToken))
            {
                if (message is not INodeMessage nodeMessage || nodeMessage.IsExpired)
                {
                    continue;
                }
                if (message.Descriptor == SubscribeEvent.Descriptor)
                {
                    var subscribeEvent = message as SubscribeEvent;
                    if (subscribeEvent == null)
                    {
                        continue;
                    }
                    subscribeEvent.Properties.TryAdd("DateTime", nodeMessage.CreatedDateTime.ToString(NodePropertyModel.DateTimeFormatString));
                    await responseStream.WriteAsync(subscribeEvent);
                }
            }
        }

        public override async Task<Empty> SendFileSystemListDirectoryResponse(FileSystemListDirectoryResponse response, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            _logger.LogInformation(response.ToString());
            await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
            return new Empty();
        }

        public override async Task<Empty> SendFileSystemListDriveResponse(FileSystemListDriveResponse response, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            _logger.LogInformation(response.ToString());
            await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
            return new Empty();
        }

        public override async Task<Empty> SendHeartBeatResponse(HeartBeatResponse response, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            if (!nodeSessionId.NodeId.IsNullOrEmpty)
            {
                await _nodeSessionService.WriteHeartBeatResponseAsync(nodeSessionId, response);
            }
            return new Empty();
        }

        public override async Task<Empty> SendFileSystemBulkOperationReport(FileSystemBulkOperationReport request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            _logger.LogInformation(request.ToString());
            await _nodeSessionService.PostMessageAsync(nodeSessionId, request);
            return new Empty();
        }

        public override async Task<Empty> SendFileSystemBulkOperationResponse(FileSystemBulkOperationResponse response, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            _logger.LogInformation(response.ToString());
            await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
            return new Empty();
        }

        public override async Task<QueryConfigurationResponse> QueryConfigurations(QueryConfigurationRequest request, ServerCallContext context)
        {
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);
            QueryConfigurationResponse queryConfigurationRsp = new QueryConfigurationResponse();
            queryConfigurationRsp.RequestId = request.RequestId;

            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var configName = request.Parameters["ConfigName"];
                string nodeId = nodeSessionId.NodeId.Value;
                var nodeInfo = await dbContext.NodeInfoDbSet.AsQueryable().FirstOrDefaultAsync(x => x.Id == nodeId);
                if (nodeInfo == null)
                {
                    queryConfigurationRsp.ErrorCode = -1;
                    queryConfigurationRsp.Message = $"Could not found node info:{nodeClientHeaders.NodeId}";
                }
                else
                {
                    JobScheduleConfigModel nodeConfigTemplate = null;
                    queryConfigurationRsp.Configurations.Add(configName,
                        nodeConfigTemplate?.ToJsonString<JobScheduleConfigModel>() ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ToString());
                queryConfigurationRsp.ErrorCode = ex.HResult;
                queryConfigurationRsp.Message = ex.Message;
            }


            return queryConfigurationRsp;
        }

        public override async Task<Empty> SendJobExecutionEventResponse(JobExecutionEventResponse response, ServerCallContext context)
        {
            var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
            NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);

            await _nodeSessionService.WriteResponseAsync(nodeSessionId, response);
            return new Empty();
        }


        public override async Task<Empty> SendJobExecutionReport(IAsyncStreamReader<JobExecutionReport> requestStream, ServerCallContext context)
        {
            try
            {
                var httpContext = context.GetHttpContext();
                var nodeClientHeaders = context.RequestHeaders.GetNodeClientHeaders();
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    try
                    {
                        var report = requestStream.Current;
                        NodeSessionId nodeSessionId = new NodeSessionId(nodeClientHeaders.NodeId);

                        await _nodeSessionService.GetInputQueue(nodeSessionId).EnqueueAsync(report);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.ToString());
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }

            return new Empty();
        }



    }
}
