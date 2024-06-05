using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.FileSystem
{
    public class NodeFileSystemWatchEventConsumerService : BackgroundService
    {
        record class NodeConfigurationDirectoryKey
        {
            public NodeConfigurationDirectoryKey(
                string nodeId,
                string configurationId,
                string directory)
            {
                NodeId = nodeId;
                ConfigurationId = configurationId;
                Directory = directory;
            }

            public string NodeId { get; set; }

            public string ConfigurationId { get; set; }

            public string Directory { get; set; }
        }

        record class DirectotyCounterInfo
        {
            public DirectotyCounterInfo()
            {
                this.PathList = new HashSet<string>();
            }

            public int ChangedCount { get; set; }

            public int DeletedCount { get; set; }

            public int RenamedCount { get; set; }

            public int CreatedCount { get; set; }

            public int TotalCount { get; set; }

            public int LastTriggerCount { get; set; }

            public DateTime LastTriggerTaskTime { get; set; }

            public HashSet<string> PathList { get; set; }

        }

        readonly ILogger<NodeFileSystemWatchEventConsumerService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<FileSystemWatchEventReportMessage> _reportMessageEventQueue;
        readonly BatchQueue<FireTaskParameters> _fireTaskParametersBatchQueue;
        readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepoFactory;
        readonly ApplicationRepositoryFactory<FileSystemWatchConfigModel> _fileSystemWatchRepoFactory;
        readonly ConcurrentDictionary<NodeConfigurationDirectoryKey, DirectotyCounterInfo> _directoryCounterDict;
        public NodeFileSystemWatchEventConsumerService(
            ILogger<NodeFileSystemWatchEventConsumerService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<FileSystemWatchEventReportMessage> reportMessageEventQueue,
            BatchQueue<FireTaskParameters> fireTaskParametersBatchQueue,
            ApplicationRepositoryFactory<JobScheduleConfigModel> taskDefinitionRepoFactory,
            ApplicationRepositoryFactory<FileSystemWatchConfigModel> fileSystemWatchRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _reportMessageEventQueue = reportMessageEventQueue;
            _fireTaskParametersBatchQueue = fireTaskParametersBatchQueue;
            _taskDefinitionRepoFactory = taskDefinitionRepoFactory;
            _fileSystemWatchRepoFactory = fileSystemWatchRepoFactory;
            _directoryCounterDict = new();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _reportMessageEventQueue.ReceiveAllAsync(stoppingToken))
            {
                try
                {
                    using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
                    using var fileSystemWatchRepo = _fileSystemWatchRepoFactory.CreateRepository();
                    foreach (var nodeEventReports in arrayPoolCollection.GroupBy(x => x.NodeSessionId.NodeId))
                    {
                        var nodeId = nodeEventReports.Key;
                        await ProcessNodeMessageReportGroupAsync(
                            nodeId,
                            taskDefinitionRepo,
                            fileSystemWatchRepo,
                            nodeEventReports,
                            stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
            }
        }

        private async Task ProcessNodeMessageReportGroupAsync(
            NodeId nodeId,
            IRepository<JobScheduleConfigModel> taskDefinitionRepo,
            IRepository<FileSystemWatchConfigModel> fileSystemWatchRepo,
            IGrouping<NodeId, FileSystemWatchEventReportMessage> nodeMessages,
            CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var reportMessageFileGroup in nodeMessages.GroupBy(GroupReportFunc))
                {
                    if (reportMessageFileGroup.Key.Directory == null)
                    {
                        foreach (var message in reportMessageFileGroup)
                        {
                            var eventReport = message.GetMessage();
                            var fileSystemWatchConfig = await fileSystemWatchRepo.GetByIdAsync(
                                eventReport.ConfigurationId,
                                cancellationToken);
                            if (fileSystemWatchConfig == null)
                            {
                                continue;
                            }
                            if (eventReport.Error != null)
                            {
                                fileSystemWatchConfig.ErrorCode = eventReport.Error.ErrorCode;
                                fileSystemWatchConfig.Message = eventReport.Error.Message;
                                await fileSystemWatchRepo.SaveChangesAsync(cancellationToken);
                            }
                        }
                    }
                    else
                    {
                        var nodeConfigurationDirectoryKey = reportMessageFileGroup.Key;
                        if (!_directoryCounterDict.TryGetValue(nodeConfigurationDirectoryKey, out var counterInfo))
                        {
                            counterInfo = new DirectotyCounterInfo();
                            _directoryCounterDict.TryAdd(nodeConfigurationDirectoryKey, counterInfo);
                        }

                        foreach (var message in reportMessageFileGroup)
                        {
                            var eventReport = message.GetMessage();
                            switch (eventReport.EventCase)
                            {
                                case FileSystemWatchEventReport.EventOneofCase.None:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Created:
                                    counterInfo.CreatedCount++;
                                    counterInfo.TotalCount++;
                                    counterInfo.PathList.Add(eventReport.Created.FullPath);
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Changed:
                                    counterInfo.ChangedCount++;
                                    counterInfo.TotalCount++;
                                    counterInfo.PathList.Add(eventReport.Changed.FullPath);
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Deleted:
                                    counterInfo.DeletedCount++;
                                    counterInfo.TotalCount++;
                                    counterInfo.PathList.Add(eventReport.Deleted.FullPath);
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Renamed:
                                    counterInfo.RenamedCount++;
                                    counterInfo.TotalCount++;
                                    counterInfo.PathList.Add(eventReport.Renamed.FullPath);
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Error:
                                    break;
                                default:
                                    break;
                            }
                        }
                        long changesCount = counterInfo.TotalCount - counterInfo.LastTriggerCount;
                        if (changesCount == 0)
                        {
                            continue;
                        }

                        var fileSystemWatchConfig = await fileSystemWatchRepo.GetByIdAsync(
                            nodeConfigurationDirectoryKey.ConfigurationId,
                            cancellationToken);


                        if (fileSystemWatchConfig == null)
                        {
                            continue;
                        }

                        if (fileSystemWatchConfig.TaskDefinitionId == null)
                        {
                            continue;
                        }

                        if (changesCount > fileSystemWatchConfig.TriggerThreshold
                            &&
                            DateTime.UtcNow - counterInfo.LastTriggerTaskTime > TimeSpan.FromSeconds(fileSystemWatchConfig.TimeThreshold))
                        {
                            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(
                                                fileSystemWatchConfig.TaskDefinitionId,
                                                cancellationToken);
                            if (taskDefinition == null)
                            {
                                continue;
                            }
                            await _fireTaskParametersBatchQueue.SendAsync(new FireTaskParameters
                            {
                                FireTimeUtc = DateTime.UtcNow,
                                TriggerSource = TaskTriggerSource.Manual,
                                FireInstanceId = $"FileSystemWatch_{Guid.NewGuid()}",
                                TaskDefinitionId = taskDefinition.Id,
                                ScheduledFireTimeUtc = DateTime.UtcNow,
                                NodeList = [new StringEntry(null, nodeId.Value)],
                                EnvironmentVariables =
                                [
                                    new StringEntry("TaskTriggerSource", nameof(NodeFileSystemWatchEventConsumerService)),
                                    new StringEntry(nameof(FileSystemWatchConfiguration.Path), fileSystemWatchConfig.Path),
                                    new StringEntry(nameof(FileSystemWatchConfiguration.RelativePath), fileSystemWatchConfig.RelativePath),
                                    new StringEntry(nameof(FtpUploadConfiguration.LocalDirectory), nodeConfigurationDirectoryKey.Directory),
                                    new StringEntry("PathList", JsonSerializer.Serialize<IEnumerable<string>>(counterInfo.PathList))
                                ]
                            }, cancellationToken);
                            counterInfo.PathList.Clear();
                            counterInfo.LastTriggerTaskTime = DateTime.UtcNow;
                            counterInfo.LastTriggerCount = counterInfo.TotalCount;
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

        NodeConfigurationDirectoryKey GroupReportFunc(FileSystemWatchEventReportMessage message)
        {
            var report = message.GetMessage();
            
            switch (report.EventCase)
            {
                case FileSystemWatchEventReport.EventOneofCase.Created:
                    {
                        string fullPath = Path.GetFullPath(report.Created.FullPath);
                        if (report.Created.Properties.ContainsKey(nameof(FileInfo)))
                        {
                            return new NodeConfigurationDirectoryKey(
                           message.NodeSessionId.NodeId.Value,
                           report.ConfigurationId,
                           Path.GetDirectoryName(fullPath) ?? fullPath);
                        }
                    }

                    break;
                case FileSystemWatchEventReport.EventOneofCase.Deleted:
                    {
                        string fullPath = Path.GetFullPath(report.Deleted.FullPath);
                        if (report.Deleted.Properties.ContainsKey(nameof(FileInfo)))
                        {
                            return new NodeConfigurationDirectoryKey(
                           message.NodeSessionId.NodeId.Value,
                           report.ConfigurationId,
                           Path.GetDirectoryName(fullPath) ?? fullPath);
                        }
                    }
                    break;
                case FileSystemWatchEventReport.EventOneofCase.Changed:
                    {
                        string fullPath = Path.GetFullPath(report.Changed.FullPath);
                        if (report.Changed.Properties.ContainsKey(nameof(FileInfo)))
                        {
                            return new NodeConfigurationDirectoryKey(
                           message.NodeSessionId.NodeId.Value,
                           report.ConfigurationId,
                           Path.GetDirectoryName(fullPath) ?? fullPath);
                        }
                    }

                    break;
                case FileSystemWatchEventReport.EventOneofCase.Renamed:
                    {
                        string fullPath = Path.GetFullPath(report.Renamed.FullPath);
                        if (report.Renamed.Properties.ContainsKey(nameof(FileInfo)))
                        {
                            return new NodeConfigurationDirectoryKey(
                           message.NodeSessionId.NodeId.Value,
                           report.ConfigurationId,
                           Path.GetDirectoryName(fullPath) ?? fullPath);
                        }
                    }
                    break;
                case FileSystemWatchEventReport.EventOneofCase.None:
                case FileSystemWatchEventReport.EventOneofCase.Error:
                default:
                    break;
            }
            return new NodeConfigurationDirectoryKey(
                message.NodeSessionId.NodeId.Value,
                report.ConfigurationId,
                null);
        }

    }
}
