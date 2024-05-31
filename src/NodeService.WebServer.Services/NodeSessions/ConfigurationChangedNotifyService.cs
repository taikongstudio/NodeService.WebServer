using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeSessions
{
    public class ConfigurationChangedNotifyService : BackgroundService
    {
        private readonly ILogger<ConfigurationChangedNotifyService> _logger;
        private readonly INodeSessionService _nodeSessionService;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly IAsyncQueue<ConfigurationChangedEvent> _eventQueue;

        public ConfigurationChangedNotifyService(
            ILogger<ConfigurationChangedNotifyService> logger,
            ExceptionCounter exceptionCounter,
            INodeSessionService nodeSessionService,
            IAsyncQueue<ConfigurationChangedEvent> eventQueue
            )
        {
            _logger = logger;
            _nodeSessionService = nodeSessionService;
            _exceptionCounter = exceptionCounter;
            _eventQueue = eventQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var configurationChangedEvent = await _eventQueue.DeuqueAsync(stoppingToken);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(NodeId.Any))
                    {
                        var report = new ConfigurationChangedReport()
                        {
                            RequestId = Guid.NewGuid().ToString(),
                            Timeout = TimeSpan.FromHours(1),
                        };
                        report.Configurations.Add(
                            Guid.NewGuid().ToString(),
                            JsonSerializer.Serialize(configurationChangedEvent));
                        await _nodeSessionService.PostMessageAsync(nodeSessionId, new SubscribeEvent()
                        {
                            RequestId = Guid.NewGuid().ToString(),
                            Topic = nameof(ConfigurationChangedNotifyService),
                            ConfigurationChangedReport = report
                        }, cancellationToken: stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogInformation(ex.ToString());
                }

            }
        }
    }
}
