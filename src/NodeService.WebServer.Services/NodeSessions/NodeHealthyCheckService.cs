using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly NodeHealthyCounterDictionary _healthyCounterDict;
    private readonly NodeHealthyCounterDictionary _healthyCounterDictionary;
    private readonly ILogger<NodeHealthyCheckService> _logger;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;


    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    private readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;

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

            List<NodeInfoModel> offlineNodeList = [];
            var nodeInfoList = await nodeInfoRepo.ListAsync(cancellationToken);
            foreach (var nodeInfo in nodeInfoList)
            {
                if (offlineNodeList.Contains(nodeInfo)) continue;
                if (nodeInfo.Status == NodeStatus.Offline
                    &&
                    DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc >
                    TimeSpan.FromMinutes(configuration.OfflineMinutes))
                {
                    var healthCounter = _healthyCounterDictionary.Ensure(new NodeSessionId(nodeInfo.Id));
                    if (healthCounter.SentNotificationCount == 0 || CanSendNotification(configuration, healthCounter))
                    {
                        healthCounter.LastSentNotificationDateTimeUtc = DateTime.UtcNow;
                        healthCounter.SentNotificationCount++;
                        offlineNodeList.Add(nodeInfo);
                    }
                }
            }

            if (offlineNodeList.Count <= 0) return;
            await SendNodeOfflineNotificationAsync(configuration, offlineNodeList, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
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

    private async Task SendNodeOfflineNotificationAsync(
        NodeHealthyCheckConfiguration configuration,
        List<NodeInfoModel> offlineNodeList,
        CancellationToken cancellationToken = default)
    {

        using var repo = _notificationRepositoryFactory.CreateRepository();

        StringBuilder stringBuilder = new StringBuilder();
        foreach (var item in offlineNodeList.Where(static x => x != null).OrderBy(static x => x.Profile.ServerUpdateTimeUtc))
        {
            stringBuilder.Append($"<tr><td>{item.Profile.ServerUpdateTimeUtc}</td><td>{item.Name}</td><td>离线</td></tr>");
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
}