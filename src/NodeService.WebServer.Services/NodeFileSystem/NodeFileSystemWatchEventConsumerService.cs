using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.FileSystem
{
    public class NodeFileSystemWatchEventConsumerService : BackgroundService
    {
        private readonly ILogger<NodeFileSystemWatchEventConsumerService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly BatchQueue<FileSystemWatchEventReportMessage> _eventQueue;

        public NodeFileSystemWatchEventConsumerService(
            ILogger<NodeFileSystemWatchEventConsumerService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<FileSystemWatchEventReportMessage> eventQueue)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _eventQueue = eventQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _eventQueue.ReceiveAllAsync(stoppingToken))
            {
                try
                {
                    foreach (var nodeEvents in arrayPoolCollection.GroupBy(x=>x.NodeSessionId.NodeId))
                    {
                        var nodeId = nodeEvents.Key;
                        foreach (var reportMessage in nodeEvents)
                        {
                            var eventReport = reportMessage.GetMessage();
                            switch (eventReport.EventCase)
                            {
                                case FileSystemWatchEventReport.EventOneofCase.None:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Created:

                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Deleted:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Changed:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Renamed:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Error:
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
            }
        }
    }
}
