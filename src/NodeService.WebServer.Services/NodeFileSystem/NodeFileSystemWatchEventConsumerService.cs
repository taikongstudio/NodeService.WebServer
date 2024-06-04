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
        record struct NodeConfigurationDirectoryKey
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

        private readonly ILogger<NodeFileSystemWatchEventConsumerService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly BatchQueue<FileSystemWatchEventReportMessage> _reportMessageEventQueue;
        private readonly BatchQueue<FireTaskParameters> _fireTaskParametersBatchQueue;
        private readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepoFactory;
        private readonly ApplicationRepositoryFactory<FileSystemWatchConfigModel> _fileSystemWatchRepoFactory;

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
                            if (fileSystemWatchConfig == null || fileSystemWatchConfig.TaskDefinitionId == null)
                            {
                                continue;
                            }
                            fileSystemWatchConfig.ErrorCode = eventReport.Error.ErrorCode;
                            fileSystemWatchConfig.Message = eventReport.Error.Message;
                            await fileSystemWatchRepo.SaveChangesAsync(cancellationToken);
                        }
                    }
                    else
                    {
                        var nodeConfigurationDirectoryKey = reportMessageFileGroup.Key;
                        long changesCount = 0;
                        foreach (var message in reportMessageFileGroup)
                        {
                            var eventReport = message.GetMessage();
                            Debug.WriteLine(eventReport.ToString());
                            switch (eventReport.EventCase)
                            {
                                case FileSystemWatchEventReport.EventOneofCase.None:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Created:
                                    changesCount++;
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Changed:
                                    changesCount++;
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Deleted:
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Renamed:
                                    changesCount++;
                                    break;
                                case FileSystemWatchEventReport.EventOneofCase.Error:
                                    break;
                                default:
                                    break;
                            }
                        }
                        if (changesCount == 0)
                        {
                            continue;
                        }
                        var fileSystemWatchConfig = await fileSystemWatchRepo.GetByIdAsync(
                            nodeConfigurationDirectoryKey.ConfigurationId,
                            cancellationToken);
                        if (fileSystemWatchConfig == null || fileSystemWatchConfig.TaskDefinitionId == null)
                        {
                            continue;
                        }

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
                                new StringEntry(nameof(FtpUploadConfiguration.LocalDirectory), nodeConfigurationDirectoryKey.Directory)
                            ]
                        }, cancellationToken);

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
                    if (report.Created.Properties.ContainsKey(nameof(FileInfo)))
                    {
                        return new NodeConfigurationDirectoryKey(
                       message.NodeSessionId.NodeId.Value,
                       report.ConfigurationId,
                       Path.GetDirectoryName(report.Created.FullPath) ?? report.Created.FullPath);
                    }
                    return new NodeConfigurationDirectoryKey(
                        message.NodeSessionId.NodeId.Value,
                        report.ConfigurationId,
                        report.Created.FullPath);
                case FileSystemWatchEventReport.EventOneofCase.Deleted:
                    if (report.Deleted.Properties.ContainsKey(nameof(FileInfo)))
                    {
                        return new NodeConfigurationDirectoryKey(
                       message.NodeSessionId.NodeId.Value,
                       report.ConfigurationId,
                       Path.GetDirectoryName(report.Deleted.FullPath) ?? report.Deleted.FullPath);
                    }
                    return new NodeConfigurationDirectoryKey(
                        message.NodeSessionId.NodeId.Value,
                        report.ConfigurationId,
                        report.Deleted.FullPath);
                case FileSystemWatchEventReport.EventOneofCase.Changed:
                    if (report.Changed.Properties.ContainsKey(nameof(FileInfo)))
                    {
                        return new NodeConfigurationDirectoryKey(
                       message.NodeSessionId.NodeId.Value,
                       report.ConfigurationId,
                       Path.GetDirectoryName(report.Changed.FullPath) ?? report.Changed.FullPath);
                    }
                    return new NodeConfigurationDirectoryKey(
                        message.NodeSessionId.NodeId.Value,
                        report.ConfigurationId,
                        report.Changed.FullPath);
                case FileSystemWatchEventReport.EventOneofCase.Renamed:
                    if (report.Renamed.Properties.ContainsKey(nameof(FileInfo)))
                    {
                        return new NodeConfigurationDirectoryKey(
                       message.NodeSessionId.NodeId.Value,
                       report.ConfigurationId,
                       Path.GetDirectoryName(report.Renamed.FullPath) ?? report.Renamed.FullPath);
                    }
                    return new NodeConfigurationDirectoryKey(
                        message.NodeSessionId.NodeId.Value,
                        report.ConfigurationId,
                        report.Renamed.FullPath);
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
