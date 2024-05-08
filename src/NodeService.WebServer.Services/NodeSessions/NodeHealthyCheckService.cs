using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckService : BackgroundService
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly ExceptionCounter _exceptionCounter;
    readonly NodeHealthyCounterDictionary _healthyCounterDict;
    readonly NodeHealthyCounterDictionary _healthyCounterDictionary;
    readonly ILogger<NodeHealthyCheckService> _logger;


    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationQueue;

    public NodeHealthyCheckService(
        ILogger<NodeHealthyCheckService> logger,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationQueue,
        NodeHealthyCounterDictionary healthyCounterDictionary,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ExceptionCounter exceptionCounter
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _notificationQueue = notificationQueue;
        _healthyCounterDictionary = healthyCounterDictionary;
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Debugger.IsAttached) await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckNodeHealthyAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    async Task CheckNodeHealthyAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var notificationSourceDictionary =
                await dbContext.PropertyBagDbSet.FindAsync(NotificationSources.NodeHealthyCheck);
            NodeHealthyCheckConfiguration? configuration;
            if (!TryReadConfiguration(notificationSourceDictionary, out configuration) || configuration == null) return;

            List<string> offlineNodeList = [];
            var nodeInfoList = await dbContext.NodeInfoDbSet.ToListAsync();
            foreach (var nodeInfo in nodeInfoList)
            {
                if (offlineNodeList.Contains(nodeInfo.Name)) continue;
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
                        offlineNodeList.Add(nodeInfo.Name);
                    }
                }
            }

            if (offlineNodeList.Count <= 0) return;
            await SendNodeOfflineNotificationAsync(dbContext, configuration, offlineNodeList, stoppingToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    bool TryReadConfiguration(
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

    async Task SendNodeOfflineNotificationAsync(
        ApplicationDbContext dbContext,
        NodeHealthyCheckConfiguration configuration,
        List<string> offlineNodeList,
        CancellationToken stoppingToken = default)
    {
        var offlineNodeNames = string.Join(",", offlineNodeList);

        foreach (var entry in configuration.Configurations)
        {
            var notificationConfig =
                await dbContext.NotificationConfigurationsDbSet.FirstOrDefaultAsync(x => x.Id == entry.Value);
            if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
            await _notificationQueue.EnqueueAsync(
                new NotificationMessage(configuration.Subject,
                    string.Format(configuration.ContentFormat, offlineNodeNames),
                    notificationConfig.Value),
                stoppingToken);
        }
    }

    static bool CanSendNotification(NodeHealthyCheckConfiguration configuration,
        NodeHealthyCounter healthCounter)
    {
        return healthCounter.SentNotificationCount > 0
               && (DateTime.UtcNow - healthCounter.LastSentNotificationDateTimeUtc) /
               TimeSpan.FromMinutes(configuration.NotificationDuration)
               >
               healthCounter.SentNotificationCount + 1;
    }
}