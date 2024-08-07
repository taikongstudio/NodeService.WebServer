using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.NodeSessions;

public partial class NodeHealthyCheckService : BackgroundService
{

    readonly ExceptionCounter _exceptionCounter;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    private readonly ObjectCache _objectCache;
    private readonly ConfigurationQueryService _configurationQueryService;
    readonly ILogger<NodeHealthyCheckService> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    readonly IAsyncQueue<NodeHealthyCheckFireEvent> _fireEventQueue;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    NodeSettings _nodeSettings;
    NodeHealthyCheckConfiguration _nodeHealthyCheckConfiguration;
    ImmutableArray<NodeUsageConfigurationModel> _nodeUsageConfigurations;

    public NodeHealthyCheckService(
        ILogger<NodeHealthyCheckService> logger,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationQueue,
        IAsyncQueue<NodeHealthyCheckFireEvent> fireEventQueue,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<NotificationConfigModel> notificationRepositoryFactory,
        NodeInfoQueryService  nodeInfoQueryService,
        ExceptionCounter exceptionCounter,
        ObjectCache objectCache,
        ConfigurationQueryService configurationQueryService
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _notificationQueue = notificationQueue;
        _fireEventQueue = fireEventQueue;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _exceptionCounter = exceptionCounter;
        _nodeInfoQueryService = nodeInfoQueryService;
        _objectCache = objectCache;
        _configurationQueryService = configurationQueryService;
        _notificationRepositoryFactory = notificationRepositoryFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        //if (Debugger.IsAttached) await Task.Delay(TimeSpan.FromHours(1));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var fireEvent = await _fireEventQueue.DeuqueAsync(cancellationToken);
                await CheckNodeHealthyAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
        }
    }

    private async Task CheckNodeHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RefreshNodeHelthyCheckConfigurationAsync(cancellationToken);
            await RefreshNodeSettingsAsync(cancellationToken);
            await RefreshNodeUsagesConfigurationListAsync(cancellationToken);
            if (_nodeHealthyCheckConfiguration == null)
            {
                return;
            }

            List<NodeHeathyCheckResult> resultList = [];
            await using var nodeInfoRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfoList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.All,
                    []),
                cancellationToken);
            foreach (var nodeInfo in nodeInfoList)
            {
                var result = await GetNodeHealthyResultAsync(nodeInfo, cancellationToken);
                if (result.Items.Count > 0)
                {
                    resultList.Add(result);
                }
            }

            if (resultList.Count <= 0) return;
            await SendNodeHealthyCheckNotificationAsync(resultList, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask<NodeHeathyCheckResult> GetNodeHealthyResultAsync(
        NodeInfoModel nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var nodeHeathyResult = new NodeHeathyCheckResult()
        {
            NodeInfo = nodeInfo,
        };
        try
        {

            if (IsNodeOffline(nodeInfo))
            {
                nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                {
                    Exception = $"守护程序离线超过{_nodeHealthyCheckConfiguration.OfflineMinutes}分钟",
                    Solution = "检查上位机是否处于运行状态",
                });
            }
            if (IsNodeScrapped(nodeInfo))
            {
                nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                {
                    Exception = $"守护程序离线超过{_nodeHealthyCheckConfiguration.ScrappMinutes}分钟",
                    Solution = $"检查上位机是否处于运行状态，如上位机已报废，请前往{_nodeHealthyCheckConfiguration.ScrappUrl}登记报废状态",
                });
            }
            if (ShouldSendTimeDiffWarning(nodeInfo))
            {
                nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                {
                    Exception = $"上位机时间与服务器时间误差大于{_nodeSettings.TimeDiffWarningSeconds}秒",
                    Solution = "建议校正上位机时间"
                });
            }
            if (_nodeHealthyCheckConfiguration.CheckProcessService)
            {
                var usageList = await ShouldSendProcessNotFoundWarningAsync(nodeInfo, cancellationToken);
                if (usageList.Any())
                {
                    nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                    {
                        Exception = $"相关进程未开启",
                        Solution = $"建议启动\"{string.Join(";", usageList)}\"相关软件或服务"
                    });
                }
            }

            var computerInfo = await _nodeInfoQueryService.Query_dl_equipment_ctrl_computer_Async(
                nodeInfo.Id,
                nodeInfo.Profile.LimsDataId,
                cancellationToken);
            if (computerInfo != null)
            {
                bool condition1 = computerInfo.Factory?.name == "博罗" && nodeInfo.Profile.FactoryName != "BL";
                bool condition2 = computerInfo.Factory?.name == "光明" && nodeInfo.Profile.FactoryName != "GM";
                if (condition1 || condition2)
                {
                    nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                    {
                        Exception = $"上位机区域信息需更新",
                        Solution = $"更新区域相关信息"
                    });
                }
            }


        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return nodeHeathyResult;
    }

    private async ValueTask<IEnumerable<string>> ShouldSendProcessNotFoundWarningAsync(
        NodeInfoModel nodeInfo,
        CancellationToken cancellationToken = default)
    {
        List<string> usageList = [];
        try
        {
            if (nodeInfo.Status == NodeStatus.Online && nodeInfo.Profile.Usages != null)
            {
                var analysisPropsResultKey = $"NodePropsAnalysisResult:{nodeInfo.Id}";
                var analysisPropsResult = await _objectCache.GetObjectAsync<AnalysisPropsResult>(analysisPropsResultKey, cancellationToken);
                if (analysisPropsResult == default)
                {
                    return usageList;
                }

                if (analysisPropsResult.ProcessInfoList == null || analysisPropsResult.ProcessInfoList.Length == 0)
                {
                    return usageList;
                }

                if (analysisPropsResult.ServiceProcessInfoList == null || analysisPropsResult.ServiceProcessInfoList.Length == 0)
                {
                    return usageList;
                }

                foreach (var nodeUsageConfiguration in _nodeUsageConfigurations)
                {
                    var foundNode = false;
                    foreach (var item in nodeUsageConfiguration.Value.Nodes)
                    {
                        if (item.NodeInfoId == nodeInfo.Id)
                        {
                            foundNode = true;
                            break;
                        }
                    }
                    if (!foundNode)
                    {
                        continue;
                    }
                    if (!analysisPropsResult.ProcessInfoList.Any(nodeUsageConfiguration.DetectProcess)
                        ||
                        !analysisPropsResult.ServiceProcessInfoList.Any(nodeUsageConfiguration.DetectServiceProcess))
                    {
                        usageList.Add(nodeUsageConfiguration.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, nodeInfo.Id);
            _logger.LogError(ex.ToString());
        }

        return usageList;
    }

    private bool IsNodeOffline(NodeInfoModel nodeInfo)
    {
        return nodeInfo.Status == NodeStatus.Offline
               &&
               DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc >
               TimeSpan.FromMinutes(_nodeHealthyCheckConfiguration.OfflineMinutes);
    }

    private bool IsNodeScrapped(NodeInfoModel nodeInfo)
    {
        return nodeInfo.Status == NodeStatus.Offline
               &&
               DateTime.UtcNow - nodeInfo.Profile.ServerUpdateTimeUtc >
               TimeSpan.FromMinutes(_nodeHealthyCheckConfiguration.ScrappMinutes);
    }

    private bool ShouldSendTimeDiffWarning(NodeInfoModel nodeInfo)
    {
        return Math.Abs((nodeInfo.Profile.ServerUpdateTimeUtc - nodeInfo.Profile.UpdateTime.ToUniversalTime())
            .TotalSeconds) > _nodeSettings.TimeDiffWarningSeconds;
    }


    async ValueTask SendNodeHealthyCheckNotificationAsync(
         List<NodeHeathyCheckResult> resultList,
        CancellationToken cancellationToken = default)
    {
        await using var repo = await _notificationRepositoryFactory.CreateRepositoryAsync(cancellationToken);
        List<NotificationConfigModel> notificationConfigList = [];
        foreach (var entry in _nodeHealthyCheckConfiguration.Configurations)
        {
            if (entry.Value == null)
            {
                continue;
            }
            var notificationConfig = await repo.GetByIdAsync(entry.Value, cancellationToken);
            if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
            notificationConfigList.Add(notificationConfig);
        }

        foreach (var group in resultList.GroupBy(static x => x.NodeInfo.Profile.FactoryName ?? AreaTags.Any))
        {
            var factoryCode = group.Key;
            string factoryName = "全部区域";
            var areaEntry = _nodeSettings.IpAddressMappings.FirstOrDefault(x => x.Tag == factoryCode);
            if (areaEntry != null)
            {
                factoryName = areaEntry.Name ?? "全部区域";
            }
            foreach (var notificationConfig in notificationConfigList)
            {
                if (notificationConfig.Value.FactoryName != factoryCode)
                {
                    continue;
                }
                var subject = _nodeHealthyCheckConfiguration.Subject
                    .Replace("$(FactoryName)", factoryName)
                    .Replace("$(DateTime)", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));

                List<EmailAttachment> attachments = [];
                foreach (var testInfoGroup in group.GroupBy(static x => x.NodeInfo.Profile.TestInfo ?? string.Empty))
                {
                    var bizType = testInfoGroup.Key;
                    if (string.IsNullOrEmpty(bizType))
                    {
                        bizType = "未分类";
                    }
                    if (!TryWriteToExcel([.. testInfoGroup], out var stream) || stream == null)
                    {
                        continue;
                    }
                    var attachmentName = _nodeHealthyCheckConfiguration.AttachmentSubject
                        .Replace("$(FactoryName)", factoryName)
                        .Replace("$(DateTime)", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"))
                        .Replace("$(BusinessType)", bizType);
                    var emailAttachment = new EmailAttachment(
                        $"{attachmentName}.xlsx",
                        "application",
                        "vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        stream);
                    attachments.Add(emailAttachment);
                }
                var content = _nodeHealthyCheckConfiguration.Content.Replace("$(FactoryName)", factoryName)
                        .Replace("$(DateTime)", DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
                var emailContent = new EmailContent(
                    subject,
                    content,
                    [.. attachments]);
                await _notificationQueue.EnqueueAsync(
                    new NotificationMessage(emailContent, notificationConfig.Value),
                    cancellationToken);
            }
        }
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

    }

    private async ValueTask RefreshNodeUsagesConfigurationListAsync(CancellationToken cancellationToken = default)
    {
        var queryResult = await _configurationQueryService.QueryConfigurationByQueryParametersAsync<NodeUsageConfigurationModel>(
            new PaginationQueryParameters()
            {
                PageIndex = 1,
                PageSize = int.MaxValue - 1,
                QueryStrategy = QueryStrategy.QueryPreferred
            },
            cancellationToken);
        _nodeUsageConfigurations = queryResult.HasValue ? queryResult.Items.ToImmutableArray() : [];
    }


    private async ValueTask RefreshNodeHelthyCheckConfigurationAsync(CancellationToken cancellationToken = default)
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

    public bool TryWriteToExcel(ImmutableArray<NodeHeathyCheckResult> nodeHeathyResults, out Stream? stream)
    {  
        stream = null;
        try
        {
            //创建工作薄  
            IWorkbook wb = new XSSFWorkbook();

            //创建一个表单
            ISheet sheet = wb.CreateSheet("上位机异常监控");
            //设置列宽
            int[] columnWidth = { 20, 20, 20, 20, 20, 20, 20, 50, 50 };
            for (int i = 0; i < columnWidth.Length; i++)
            {
                //设置列宽度，256*字符数，因为单位是1/256个字符
                sheet.SetColumnWidth(i, 256 * columnWidth[i]);
            }

            //测试数据

            IRow headerRow = sheet.CreateRow(0);
            var headers = new string[] { "最后在线时间", "上位机名称", "业务类型", "实验室名称", "测试区域", "上位机负责人", "IP地址", "异常消息", "建议处理措施" };
            {
                for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    var cell = headerRow.CreateCell(columnIndex);//创建第j列
                    SetCellValue<string>(cell, headers[columnIndex]);
                }
            }


            var rowIndex = 1;
            for (int dataIndex = 0; dataIndex < nodeHeathyResults.Length; dataIndex++)
            {
                var result = nodeHeathyResults[dataIndex];

                foreach (var item in result.Items)
                {
                    var dataRow = sheet.CreateRow(rowIndex);
                    rowIndex++;
                    for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    {
                        var cell = dataRow.CreateCell(columnIndex);
                        switch (columnIndex)
                        {
                            case 0:
                                SetCellValue(cell, result.NodeInfo.Profile.ServerUpdateTimeUtc);
                                break;
                            case 1:
                                SetCellValue(cell, result.NodeInfo.Profile.Name ?? string.Empty);
                                break;
                            case 2:
                                SetCellValue(cell, result.NodeInfo.Profile.TestInfo ?? string.Empty);
                                break;
                            case 4:
                                SetCellValue(cell, result.NodeInfo.Profile.LabName ?? string.Empty);
                                break;
                            case 3:
                                SetCellValue(cell, result.NodeInfo.Profile.LabArea ?? string.Empty);
                                break;
                            case 5:
                                SetCellValue(cell, result.NodeInfo.Profile.Manager ?? string.Empty);
                                break;
                            case 6:
                                SetCellValue(cell, result.NodeInfo.Profile.IpAddress ?? string.Empty);
                                break;
                            case 7:
                                SetCellValue(cell, item.Exception);
                                break;
                            case 8:
                                SetCellValue(cell, item.Solution);
                                break;
                            default:
                                break;
                        }
                    }

                }
            }

            stream = new MemoryStream();
            wb.Write(stream, true);//向打开的这个Excel文件中写入表单并保存。  
            stream.Position = 0;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
        return false;
    }

    public static void SetCellValue<T>(ICell cell, T obj)
    {
        if (obj is int intValue)
        {
            cell.SetCellValue(intValue);
        }
        else if (obj is double doubleValue)
        {
            cell.SetCellValue(doubleValue);
        }
        else if (obj is IRichTextString richTextString)
        {
            cell.SetCellValue(richTextString);
        }
        else if (obj is string stringValue)
        {
            cell.SetCellValue(stringValue);
        }
        else if (obj is DateTime dateTimeValue)
        {
            cell.SetCellValue(dateTimeValue.ToString("yyyy/MM/dd HH:mm:ss"));
        }
        else if (obj is bool boolValue)
        {
            cell.SetCellValue(boolValue);
        }
        else
        {
            cell.SetCellValue(obj.ToString());
        }
    }
}