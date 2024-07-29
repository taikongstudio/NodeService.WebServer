using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckService : BackgroundService
{
    private class NodeHealthyCheckItem
    {
        public NodeInfoModel Node { get; set; }

        public string Message { get; set; }
    }

    private readonly ExceptionCounter _exceptionCounter;
    private readonly NodeHealthyCounterDictionary _healthyCounterDict;
    private readonly NodeHealthyCounterDictionary _healthyCounterDictionary;
    private readonly ILogger<NodeHealthyCheckService> _logger;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    private readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    private NodeSettings _nodeSettings;
    private NodeHealthyCheckConfiguration _nodeHealthyCheckConfiguration;
    private string _timeDiffWarningMsg;

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
        if (Debugger.IsAttached) await Task.Delay(TimeSpan.FromHours(1));
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
            await RefreshNodeConfigurationAsync(cancellationToken);
            await RefreshNodeSettingsAsync(cancellationToken);

            List<NodeHealthyCheckItem> nodeHealthyCheckItemList = [];
            await using var nodeInfoRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync();
            var nodeInfoList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.All,
                    []),
                cancellationToken);
            foreach (var nodeInfo in nodeInfoList)
            {
                var healthyCheckItems = GetNodeHealthyCheckItems(nodeInfo);
                if (healthyCheckItems.Any())
                {
                    var healthyCounter = _healthyCounterDictionary.Ensure(new NodeId(nodeInfo.Id));
                    if (healthyCounter.SentNotificationCount == 0 || CanSendNotification(healthyCounter))
                    {
                        healthyCounter.LastSentNotificationDateTimeUtc = DateTime.UtcNow;
                        healthyCounter.SentNotificationCount++;
                        nodeHealthyCheckItemList.AddRange(healthyCheckItems);
                    }
                }
            }

            if (nodeHealthyCheckItemList.Count <= 0) return;
            await SendNodeHealthyCheckNotificationAsync(nodeHealthyCheckItemList, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private IEnumerable<NodeHealthyCheckItem> GetNodeHealthyCheckItems(NodeInfoModel nodeInfo)
    {
        if (IsNodeOffline(nodeInfo))
            yield return new NodeHealthyCheckItem()
            {
                Node = nodeInfo,
                Message = "离线"
            };
        if (ShouldSendTimeDiffWarning(nodeInfo))
            yield return new NodeHealthyCheckItem()
            {
                Node = nodeInfo,
                Message = _timeDiffWarningMsg
            };
        yield break;
    }

    private bool IsNodeOffline(NodeInfoModel nodeInfo)
    {
        return nodeInfo.Status == NodeStatus.Offline
               &&
               DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc >
               TimeSpan.FromMinutes(_nodeHealthyCheckConfiguration.OfflineMinutes);
    }

    private bool ShouldSendTimeDiffWarning(NodeInfoModel nodeInfo)
    {
        return Math.Abs((nodeInfo.Profile.ServerUpdateTimeUtc - nodeInfo.Profile.UpdateTime.ToUniversalTime())
            .TotalSeconds) > _nodeSettings.TimeDiffWarningSeconds;
    }


    private async Task SendNodeHealthyCheckNotificationAsync(
        List<NodeHealthyCheckItem> nodeHealthyCheckItemList,
        CancellationToken cancellationToken = default)
    {
        await using var repo = await _notificationRepositoryFactory.CreateRepositoryAsync();

        var stringBuilder = new StringBuilder();
        List<StringEntry> stringEntries = [];
        foreach (var nodeHealthyCheckItemGroups in nodeHealthyCheckItemList.GroupBy(static x => x.Node))
        {
            foreach (var nodeHealthCheckItem in nodeHealthyCheckItemGroups.OrderBy(static x =>
             x.Node.Profile.ServerUpdateTimeUtc))
            {
                stringBuilder.Append($"<tr><td>{nodeHealthCheckItem.Node.Profile.ServerUpdateTimeUtc}</td><td>{nodeHealthCheckItem.Node.Name}</td><td>{nodeHealthCheckItem.Message}</td></tr>");
                stringEntries.Add(new StringEntry(nodeHealthCheckItem.Node.Profile.ServerUpdateTimeUtc.ToString(), nodeHealthCheckItem.Node.Name));
            }
        }

        var content = _nodeHealthyCheckConfiguration.ContentFormat.Replace("{0}", stringBuilder.ToString());


        foreach (var entry in _nodeHealthyCheckConfiguration.Configurations)
        {
            var notificationConfig = await repo.GetByIdAsync(entry.Value, cancellationToken);
            if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
            await _notificationQueue.EnqueueAsync(
                new NotificationMessage(_nodeHealthyCheckConfiguration.Subject,
                    content,
                    notificationConfig.Value),
                cancellationToken);

            await _notificationQueue.EnqueueAsync(
            new NotificationMessage(_nodeHealthyCheckConfiguration.Subject,
                stringEntries,
                notificationConfig.Value),
            cancellationToken);
        }
    }

    private bool CanSendNotification(NodeHealthyCounter healthCounter)
    {
        return healthCounter.SentNotificationCount > 0
               && (DateTime.UtcNow - healthCounter.LastSentNotificationDateTimeUtc) /
               TimeSpan.FromMinutes(_nodeHealthyCheckConfiguration.NotificationDuration)
               >
               healthCounter.SentNotificationCount + 1;
    }

    private async Task RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var repo = await _propertyBagRepositoryFactory.CreateRepositoryAsync();
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
        if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();

        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);

        _timeDiffWarningMsg = $"服务与节点时间差异大于{_nodeSettings.TimeDiffWarningSeconds}秒";
    }


    private async Task RefreshNodeConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await using var propertyBagRepo = await _propertyBagRepositoryFactory.CreateRepositoryAsync();
        await using var nodeInfoRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync();
        var propertyBag =
            await propertyBagRepo.FirstOrDefaultAsync(
                new PropertyBagSpecification(NotificationSources.NodeHealthyCheck), cancellationToken);

        try
        {
            if (propertyBag == null
                ||
                !propertyBag.TryGetValue("Value", out var value)
                || value is not string json
               )
                _nodeHealthyCheckConfiguration = new NodeHealthyCheckConfiguration();
            else
                _nodeHealthyCheckConfiguration = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(json);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }
}