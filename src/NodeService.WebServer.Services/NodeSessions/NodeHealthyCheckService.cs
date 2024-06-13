using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System.Configuration;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckService : BackgroundService
{
    class NodeHealthyCheckItem
    {
        public NodeInfoModel Node { get; set; }

        public string Message { get; set; }
    }
    private readonly ExceptionCounter _exceptionCounter;
    private readonly NodeHealthyCounterDictionary _healthyCounterDict;
    private readonly NodeHealthyCounterDictionary _healthyCounterDictionary;
    private readonly ILogger<NodeHealthyCheckService> _logger;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;


    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    private readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    private NodeSettings _nodeSettings;

    public NodeHealthyCheckService(
        ILogger<NodeHealthyCheckService> logger,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationQueue,
        NodeHealthyCounterDictionary healthyCounterDictionary,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<NotificationConfigModel> notificationRepositoryFactory,
        ExceptionCounter exceptionCounter
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _notificationQueue = notificationQueue;
        _healthyCounterDictionary = healthyCounterDictionary;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _notificationRepositoryFactory = notificationRepositoryFactory;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!Debugger.IsAttached) await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckNodeHealthyAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
    }

    private async Task CheckNodeHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var propertyBagRepo = _propertyBagRepositoryFactory.CreateRepository();
            using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
            var propertyBag =
                await propertyBagRepo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.NodeHealthyCheck), cancellationToken);
            NodeHealthyCheckConfiguration? configuration;
            if (!TryReadConfiguration(propertyBag, out configuration) || configuration == null) return;

            await RefreshNodeSettingsAsync(cancellationToken);

            List<NodeHealthyCheckItem> nodeHealthyCheckItemList = [];
            var nodeInfoList = await nodeInfoRepo.ListAsync(cancellationToken);
            foreach (var nodeInfo in nodeInfoList)
            {
                var healthyCheckItems = GetNodeHealthyCheckItems(configuration, nodeInfo);
                if (healthyCheckItems.Any())
                {
                    var healthyCounter = _healthyCounterDictionary.Ensure(new NodeId(nodeInfo.Id));
                    if (healthyCounter.SentNotificationCount == 0 || CanSendNotification(configuration, healthyCounter))
                    {
                        healthyCounter.LastSentNotificationDateTimeUtc = DateTime.UtcNow;
                        healthyCounter.SentNotificationCount++;
                        nodeHealthyCheckItemList.AddRange(healthyCheckItems);
                    }
                }
            }

            if (nodeHealthyCheckItemList.Count <= 0) return;
            await SendNodeHealthyCheckNotificationAsync(configuration, nodeHealthyCheckItemList, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    IEnumerable<NodeHealthyCheckItem> GetNodeHealthyCheckItems(NodeHealthyCheckConfiguration configuration, NodeInfoModel nodeInfo)
    {
        if (IsNodeOffline(configuration, nodeInfo))
        {
            yield return new NodeHealthyCheckItem()
            {
                Node = nodeInfo,
                Message = "离线"
            };
        }
        if (ShouldSendTimeDiffWarning(nodeInfo))
        {
            yield return new NodeHealthyCheckItem()
            {
                Node = nodeInfo,
                Message = $"服务与节点时间差异大于{_nodeSettings.TimeDiffWarningSeconds}秒"
            };
        }
        yield break;
    }

    bool IsNodeOffline(NodeHealthyCheckConfiguration configuration, NodeInfoModel nodeInfo)
    {
        return nodeInfo.Status == NodeStatus.Offline
                              &&
                              DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc >
                              TimeSpan.FromMinutes(configuration.OfflineMinutes);
    }

    bool ShouldSendTimeDiffWarning(NodeInfoModel nodeInfo)
    {
        return Math.Abs((nodeInfo.Profile.ServerUpdateTimeUtc - nodeInfo.Profile.UpdateTime.ToUniversalTime()).TotalSeconds) > _nodeSettings.TimeDiffWarningSeconds;
    }

    private bool TryReadConfiguration(
        Dictionary<string, object>? notificationSourceDictionary,
        out NodeHealthyCheckConfiguration? configuration)
    {
        configuration = null;
        try
        {
            if (notificationSourceDictionary == null
                ||
                !notificationSourceDictionary.TryGetValue("Value", out var value)
                || value is not string json
               )
                return false;
            configuration = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(json);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return configuration != null;
    }

    private async Task SendNodeHealthyCheckNotificationAsync(
        NodeHealthyCheckConfiguration configuration,
        List<NodeHealthyCheckItem> nodeHealthyCheckItemList,
        CancellationToken cancellationToken = default)
    {

        using var repo = _notificationRepositoryFactory.CreateRepository();

        StringBuilder stringBuilder = new StringBuilder();
        foreach (var nodeHealthyCheckItemGroups in nodeHealthyCheckItemList.GroupBy(static x => x.Node))
        {
            foreach (var nodeHealthCheckItem in nodeHealthyCheckItemGroups.OrderBy(x => x.Node.Profile.ServerUpdateTimeUtc))
            {
                stringBuilder.Append($"<tr><td>{nodeHealthCheckItem.Node.Profile.ServerUpdateTimeUtc}</td><td>{nodeHealthCheckItem.Node.Name}</td><td>{nodeHealthCheckItem.Message}</td></tr>");

            }
        }
        var content = configuration.ContentFormat.Replace("{0}", stringBuilder.ToString());


        foreach (var entry in configuration.Configurations)
        {
            var notificationConfig = await repo.GetByIdAsync(entry.Value, cancellationToken);
            if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
            await _notificationQueue.EnqueueAsync(
                new NotificationMessage(configuration.Subject,
                    content,
                    notificationConfig.Value),
                cancellationToken);
        }
    }

    private static bool CanSendNotification(NodeHealthyCheckConfiguration configuration,
        NodeHealthyCounter healthCounter)
    {
        return healthCounter.SentNotificationCount > 0
               && (DateTime.UtcNow - healthCounter.LastSentNotificationDateTimeUtc) /
               TimeSpan.FromMinutes(configuration.NotificationDuration)
               >
               healthCounter.SentNotificationCount + 1;
    }

    private async Task RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var repo = _propertyBagRepositoryFactory.CreateRepository();
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
        if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();
        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);
    }
}