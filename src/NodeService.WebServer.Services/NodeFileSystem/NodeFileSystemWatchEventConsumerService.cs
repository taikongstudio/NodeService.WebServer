using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class NodeFileSystemWatchEventConsumerService : BackgroundService
{
    private readonly ILogger<NodeFileSystemWatchEventConsumerService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<FileSystemWatchEventReportMessage> _reportMessageEventQueue;
    private readonly BatchQueue<FireTaskParameters> _fireTaskParametersBatchQueue;
    private readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    private readonly ApplicationRepositoryFactory<FileSystemWatchConfigModel> _fileSystemWatchRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ConcurrentDictionary<NodeConfigurationDirectoryKey, DirectoryCounterInfo> _directoryCounterDict;

    public NodeFileSystemWatchEventConsumerService(
        ILogger<NodeFileSystemWatchEventConsumerService> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<FileSystemWatchEventReportMessage> reportMessageEventQueue,
        BatchQueue<FireTaskParameters> fireTaskParametersBatchQueue,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<FileSystemWatchConfigModel> fileSystemWatchRepoFactory)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _reportMessageEventQueue = reportMessageEventQueue;
        _fireTaskParametersBatchQueue = fireTaskParametersBatchQueue;
        _taskDefinitionRepoFactory = taskDefinitionRepoFactory;
        _fileSystemWatchRepoFactory = fileSystemWatchRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _directoryCounterDict = new ConcurrentDictionary<NodeConfigurationDirectoryKey, DirectoryCounterInfo>();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Start");
        await foreach (var arrayPoolCollection in _reportMessageEventQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
                using var fileSystemWatchRepo = _fileSystemWatchRepoFactory.CreateRepository();
                foreach (var nodeEventReports in arrayPoolCollection.GroupBy(static x => x.NodeSessionId.NodeId))
                {
                    var nodeId = nodeEventReports.Key;
                    var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId.Value, cancellationToken);
                    if (nodeInfo == null) continue;
                    await ProcessNodeMessageReportListAsync(
                        nodeInfo,
                        taskDefinitionRepo,
                        fileSystemWatchRepo,
                        nodeEventReports,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    private async Task ProcessNodeMessageReportListAsync(
        NodeInfoModel nodeInfo,
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IRepository<FileSystemWatchConfigModel> fileSystemWatchRepo,
        IEnumerable<FileSystemWatchEventReportMessage> nodeMessages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var reportMessageFileGroup in nodeMessages.GroupBy(GroupReportFunc))
                if (reportMessageFileGroup.Key.Directory == null)
                {
                    foreach (var message in reportMessageFileGroup)
                    {
                        var eventReport = message.GetMessage();
                        var fileSystemWatchConfig = await fileSystemWatchRepo.GetByIdAsync(
                            eventReport.ConfigurationId,
                            cancellationToken);
                        if (fileSystemWatchConfig == null) continue;
                        StringEntry nodeFeedback = default;
                        if (fileSystemWatchConfig.Feedbacks == null) fileSystemWatchConfig.Feedbacks = [];

                        if (fileSystemWatchConfig.Feedbacks.Count > 0)
                        {
                            var index = 0;
                            foreach (var feedback in fileSystemWatchConfig.Feedbacks)
                            {
                                if (feedback.Tag == nodeInfo.Id)
                                {
                                    nodeFeedback = feedback;
                                    break;
                                }

                                index++;
                            }

                            if (nodeFeedback != null)
                            {
                                var list = fileSystemWatchConfig.Feedbacks.ToList();
                                nodeFeedback = new StringEntry()
                                {
                                    Name = nodeFeedback.Name,
                                    Value = nodeFeedback.Value,
                                    Tag = nodeFeedback.Tag
                                };
                                list[index] = nodeFeedback;
                                fileSystemWatchConfig.Feedbacks = list;
                            }
                        }

                        if (nodeFeedback == null)
                        {
                            nodeFeedback = new StringEntry()
                            {
                                Name = nodeInfo.Name,
                                Value = string.Empty,
                                Tag = nodeInfo.Id
                            };
                            fileSystemWatchConfig.Feedbacks = [.. fileSystemWatchConfig.Feedbacks, nodeFeedback];
                        }

                        if (eventReport.Error == null)
                            nodeFeedback.Value = "Success";
                        else
                            nodeFeedback.Value =
                                $"ErrorCode:{eventReport.Error.ErrorCode},ErrorMessage:{eventReport.Error.Message}";
                        await fileSystemWatchRepo.UpdateAsync(fileSystemWatchConfig, cancellationToken);
                    }
                }
                else
                {
                    var nodeConfigurationDirectoryKey = reportMessageFileGroup.Key;
                    if (!_directoryCounterDict.TryGetValue(nodeConfigurationDirectoryKey, out var counterInfo))
                    {
                        counterInfo = new DirectoryCounterInfo(nodeConfigurationDirectoryKey.Directory);
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
                    if (changesCount == 0) continue;

                    var fileSystemWatchConfig = await fileSystemWatchRepo.GetByIdAsync(
                        nodeConfigurationDirectoryKey.ConfigurationId,
                        cancellationToken);


                    if (fileSystemWatchConfig == null) continue;
                    if (fileSystemWatchConfig.HandlerContext == null) continue;

                    if (changesCount > fileSystemWatchConfig.TriggerThreshold
                        &&
                        DateTime.UtcNow - counterInfo.LastTriggerTaskTime >
                        TimeSpan.FromSeconds(fileSystemWatchConfig.TimeThreshold))
                    {
                        switch (fileSystemWatchConfig.EventHandler)
                        {
                            case FileSystemWatchEventHandler.Taskflow:
                                var taskDefinition = await taskDefinitionRepo.GetByIdAsync(
                                    fileSystemWatchConfig.HandlerContext,
                                    cancellationToken);
                                if (taskDefinition == null) continue;
                                await _fireTaskParametersBatchQueue.SendAsync(new FireTaskParameters
                                {
                                    FireTimeUtc = DateTime.UtcNow,
                                    TriggerSource = TaskTriggerSource.Manual,
                                    FireInstanceId = $"FileSystemWatch_{Guid.NewGuid()}",
                                    TaskDefinitionId = taskDefinition.Id,
                                    ScheduledFireTimeUtc = DateTime.UtcNow,
                                    NodeList = [new StringEntry(null, nodeInfo.Id)],
                                    EnvironmentVariables =
                                    [
                                        new StringEntry("TaskTriggerSource",
                                            nameof(NodeFileSystemWatchEventConsumerService)),
                                        new StringEntry(nameof(FileSystemWatchConfiguration.Path),
                                            fileSystemWatchConfig.Path),
                                        new StringEntry(nameof(FileSystemWatchConfiguration.RelativePath),
                                            fileSystemWatchConfig.RelativePath),
                                        new StringEntry(nameof(FtpUploadConfiguration.LocalDirectory),
                                            nodeConfigurationDirectoryKey.Directory),
                                        new StringEntry("PathList",
                                            JsonSerializer.Serialize<IEnumerable<string>>(counterInfo.PathList))
                                    ]
                                }, cancellationToken);
                                break;
                            case FileSystemWatchEventHandler.AutoSync:

                                break;
                            default:
                                break;
                        }

                        counterInfo.PathList.Clear();
                        counterInfo.LastTriggerTaskTime = DateTime.UtcNow;
                        counterInfo.LastTriggerCount = counterInfo.TotalCount;
                    }
                }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private NodeConfigurationDirectoryKey GroupReportFunc(FileSystemWatchEventReportMessage message)
    {
        return NodeConfigurationDirectoryKey.Create(message.GetMessage(), message.NodeSessionId.NodeId.Value);
    }
}