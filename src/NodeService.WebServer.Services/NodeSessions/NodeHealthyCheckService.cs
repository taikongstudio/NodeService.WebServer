using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;

namespace NodeService.WebServer.Services.NodeSessions
{
    public class NodeHealthyCheckService : BackgroundService
    {


        private readonly INodeSessionService _nodeSessionService;
        private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
        private readonly NodeHealthyCounterDictionary _healthyCounterDictionary;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<NodeHealthyCheckService> _logger;
        private readonly NodeHealthyCounterDictionary _healthyCounterDict;

        public NodeHealthyCheckService(
            ILogger<NodeHealthyCheckService> logger,
            INodeSessionService nodeSessionService,
            IAsyncQueue<NotificationMessage> notificationQueue,
            NodeHealthyCounterDictionary healthyCounterDictionary,
            IDbContextFactory<ApplicationDbContext> dbContextFactory
            )
        {
            _logger = logger;
            _nodeSessionService = nodeSessionService;
            _notificationQueue = notificationQueue;
            _healthyCounterDictionary = healthyCounterDictionary;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckNodeHealthy(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

        }

        private async Task CheckNodeHealthy(CancellationToken stoppingToken)
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();

                var notificationSourceDictionary = await dbContext.PropertyBagDbSet.FindAsync(NotificationSources.NodeHealthyCheck);
                NodeHealthyCheckConfiguration? configuration;
                if (!TryReadConfiguration(notificationSourceDictionary, out configuration) || configuration == null)
                {
                    return;
                }

                List<string> offlineNodeList = [];
                var nodeInfoList = await dbContext.NodeInfoDbSet.ToListAsync();
                foreach (var nodeInfo in nodeInfoList)
                {
                    if (offlineNodeList.Contains(nodeInfo.Name))
                    {
                        continue;
                    }
                    if (nodeInfo.Status == NodeStatus.Offline
                        &&
                        DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc > TimeSpan.FromMinutes(configuration.OfflineMinutes))
                    {

                        var healthCounter = _healthyCounterDictionary.Ensure(new(nodeInfo.Id));
                        if (healthCounter.SentNotificationCount == 0 || CanSendNotification(configuration, healthCounter))
                        {
                            healthCounter.LastSentNotificationDateTimeUtc = DateTime.UtcNow;
                            healthCounter.SentNotificationCount++;
                            offlineNodeList.Add(nodeInfo.Name);
                        }
                    }
                }
                if (offlineNodeList.Count <= 0)
                {
                    return;
                }
                await SendNodeOfflineNotificationAsync(dbContext, configuration, offlineNodeList, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }


        }

        private static bool TryReadConfiguration(
            Dictionary<string, object>? notificationSourceDictionary,
            out NodeHealthyCheckConfiguration? configuration)
        {
            configuration = null;
            if (notificationSourceDictionary == null
                ||
                !notificationSourceDictionary.TryGetValue("Value", out var value)
                || value is not string json
                )
            {
                return false;
            }
            configuration = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(json);
            return configuration != null;
        }

        private async Task SendNodeOfflineNotificationAsync(
            ApplicationDbContext dbContext,
            NodeHealthyCheckConfiguration configuration,
            List<string> offlineNodeList,
            CancellationToken stoppingToken = default)
        {
            var offlineNodeNames = string.Join(",", offlineNodeList);

            foreach (var entry in configuration.Configurations)
            {
                var notificationConfig = await dbContext.NotificationConfigurationsDbSet.FirstOrDefaultAsync(x => x.Id == entry.Value);
                if (notificationConfig == null || !notificationConfig.IsEnabled)
                {
                    continue;
                }
                await _notificationQueue.EnqueueAsync(
                    new(configuration.Subject,
                    string.Format(configuration.ContentFormat, offlineNodeNames),
                    notificationConfig.Value),
                    stoppingToken);
            }
        }

        private static bool CanSendNotification(NodeHealthyCheckConfiguration configuration, NodeHealthyCounter healthCounter)
        {
            return healthCounter.SentNotificationCount > 0
                && (DateTime.UtcNow - healthCounter.LastSentNotificationDateTimeUtc) / TimeSpan.FromHours(configuration.NotificationDuration)
                >
                (healthCounter.SentNotificationCount + 1);
        }
    }
}
