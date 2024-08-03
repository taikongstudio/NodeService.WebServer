using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using NPOI.HSSF.UserModel;
using NPOI.HSSF.Util;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.Util;
using NPOI.XSSF.UserModel;

using System.Data;
using System.Threading;

namespace NodeService.WebServer.Services.NodeSessions;

public partial class NodeHealthyCheckService : BackgroundService
{

    readonly ExceptionCounter _exceptionCounter;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    private readonly ObjectCache _objectCache;
    readonly ILogger<NodeHealthyCheckService> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    readonly IAsyncQueue<NodeHealthyCheckFireEvent> _fireEventQueue;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<NotificationConfigModel> _notificationRepositoryFactory;
    readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    NodeSettings _nodeSettings;
    NodeHealthyCheckConfiguration _nodeHealthyCheckConfiguration;

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
        ObjectCache objectCache
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _notificationQueue = notificationQueue;
        _fireEventQueue = fireEventQueue;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _notificationRepositoryFactory = notificationRepositoryFactory;
        _exceptionCounter = exceptionCounter;
        _nodeInfoQueryService = nodeInfoQueryService;
        _objectCache = objectCache;
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

            if (_nodeHealthyCheckConfiguration == null)
            {
                return;
            }

            List<NodeHeathyResult> resultList = [];
            await using var nodeInfoRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync();
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

    private async ValueTask<NodeHeathyResult> GetNodeHealthyResultAsync(
        NodeInfoModel nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var nodeHeathyResult = new NodeHeathyResult()
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
            if (ShouldSendTimeDiffWarning(nodeInfo))
            {
                nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                {
                    Exception = $"上位机时间与服务器时间误差大于{_nodeSettings.TimeDiffWarningSeconds}秒",
                    Solution = "建议校正计算机时间"
                });
            }
            var usageList = await ShouldSendProcessNotFoundWarningAsync(nodeInfo, cancellationToken);
            foreach (var usage in usageList)
            {
                nodeHeathyResult.Items.Add(new NodeHealthyCheckItem()
                {
                    Exception = $"相关进程未开启",
                    Solution = $"建议启动\"{usage}\"相关软件或服务"
                });
            }

            var computerInfo = await _nodeInfoQueryService.Query_dl_equipment_ctrl_computer_Async(nodeInfo.Id, cancellationToken);
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
                IEnumerable<ProcessInfo> processInfoList = [];
                var analysisPropsResultKey = $"NodePropsAnalysisResult:{nodeInfo.Id}";
                var analysisPropsResult = await _objectCache.GetObjectAsync<AnalysisPropsResult>(analysisPropsResultKey, cancellationToken);
                processInfoList = analysisPropsResult.ProcessInfoList;

                if (processInfoList == null || !processInfoList.Any())
                {
                    return usageList;
                }

                foreach (var usage in nodeInfo.Profile.Usages.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var usageCount = 0;
                    var processExitsCount = 0;
                    foreach (var item in _nodeSettings.ProcessUsagesMapping)
                    {
                        if (string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Value))
                        {
                            continue;
                        }
                        if (usage != item.Value)
                        {
                            continue;
                        }
                        usageCount++;
                        if (processInfoList != null)
                        {
                            foreach (var processInfo in processInfoList)
                            {
                                if (processInfo.FileName.Contains(item.Name))
                                {
                                    processExitsCount++;
                                }
                            }
                        }
                    }

                    if (usageCount > 0 && processExitsCount == 0)
                    {
                        usageList.Add(usage);
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

    private async ValueTask<IEnumerable<ProcessInfo>> GetProcessListFromDbAsync(string nodeInfoId, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodePropsSnapshot = await _nodeInfoQueryService.QueryNodePropsAsync(nodeInfoId, true, default);
            if (nodePropsSnapshot != null)
            {
                var processEntry = nodePropsSnapshot.NodeProperties.FirstOrDefault(static x => x.Name == NodePropertyModel.Process_Processes_Key);

                if (processEntry != default && processEntry.Value != null && processEntry.Value.Contains('[', StringComparison.CurrentCulture))
                {
                    var processInfoList = JsonSerializer.Deserialize<ProcessInfo[]?>(processEntry.Value);
                    return processInfoList;
                }
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, nodeInfoId);
        }

        return null;
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


    async ValueTask SendNodeHealthyCheckNotificationAsync(
         List<NodeHeathyResult> resultList,
        CancellationToken cancellationToken = default)
    {

        if (!TryWriteToExcel(resultList, out var stream) || stream == null)
        {
            return;
        }

        var emailAttachment = new EmailAttachment(
            $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.xlsx",
            "application",
            "vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            stream);


        var content = _nodeHealthyCheckConfiguration.ContentFormat.Replace("{0}", string.Empty);

        await using var repo = await _notificationRepositoryFactory.CreateRepositoryAsync(cancellationToken);

        foreach (var entry in _nodeHealthyCheckConfiguration.Configurations)
        {

            var notificationConfig = await repo.GetByIdAsync(entry.Value, cancellationToken);
            if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
            await _notificationQueue.EnqueueAsync(
                    new NotificationMessage(new EmailContent(_nodeHealthyCheckConfiguration.Subject, content, [emailAttachment]),
                    notificationConfig.Value),
                cancellationToken);

            await _notificationQueue.EnqueueAsync(
            new NotificationMessage(new LarkContent()
            {
                Subject = $"{_nodeHealthyCheckConfiguration.Subject} {DateTime.Now:yyyy-MM-dd}",
                Entries = []
            },
                notificationConfig.Value),
            cancellationToken);
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


    private async Task RefreshNodeHelthyCheckConfigurationAsync(CancellationToken cancellationToken = default)
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

    public bool TryWriteToExcel(List<NodeHeathyResult> nodeHeathyResults, out Stream? stream)
    {  
        stream = null;
        try
        {
            //创建工作薄  
            IWorkbook wb = new XSSFWorkbook();

            ICellStyle style1 = wb.CreateCellStyle();//样式
            style1.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Left;//文字水平对齐方式
            style1.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;//文字垂直对齐方式
                                                                                  //设置边框
            style1.BorderBottom = NPOI.SS.UserModel.BorderStyle.Thin;
            style1.BorderLeft = NPOI.SS.UserModel.BorderStyle.Thin;
            style1.BorderRight = NPOI.SS.UserModel.BorderStyle.Thin;
            style1.BorderTop = NPOI.SS.UserModel.BorderStyle.Thin;
            style1.WrapText = true;//自动换行

            ICellStyle style2 = wb.CreateCellStyle();//样式
            IFont font1 = wb.CreateFont();//字体
            font1.FontName = "Microsoft YaHei";
            font1.Color = HSSFColor.Red.Index;//字体颜色
            font1.Boldweight = (short)FontBoldWeight.Normal;//字体加粗样式
            style2.SetFont(font1);//样式里的字体设置具体的字体样式
                                  //设置背景色
            style2.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
            style2.FillPattern = FillPattern.SolidForeground;
            style2.FillBackgroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
            style2.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Left;//文字水平对齐方式
            style2.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;//文字垂直对齐方式

            ICellStyle dateStyle = wb.CreateCellStyle();//样式
            dateStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Left;//文字水平对齐方式
            dateStyle.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;//文字垂直对齐方式
                                                                                     //设置数据显示格式
            IDataFormat dataFormatCustom = wb.CreateDataFormat();
            dateStyle.DataFormat = dataFormatCustom.GetFormat("yyyy-MM-dd HH:mm:ss");

            //创建一个表单
            ISheet sheet = wb.CreateSheet("Sheet0");
            //设置列宽
            int[] columnWidth = { 20, 20, 20, 20, 20, 20, 20, 50, 50 };
            for (int i = 0; i < columnWidth.Length; i++)
            {
                //设置列宽度，256*字符数，因为单位是1/256个字符
                sheet.SetColumnWidth(i, 256 * columnWidth[i]);
            }

            //测试数据

            IRow headerRow = sheet.CreateRow(0);
            var headers = new string[] { "最后在线时间", "上位机名称", "测试区域", "实验室区域", "实验室名称", "上位机负责人", "IP地址", "异常消息", "建议处理措施" };
            {
                for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    var cell = headerRow.CreateCell(columnIndex);//创建第j列
                    SetCellValue<string>(cell, headers[columnIndex]);
                }
            }


            var rowIndex = 0;
            for (int dataIndex = 0; dataIndex < nodeHeathyResults.Count; dataIndex++)
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
                                cell.CellStyle = dateStyle;
                                break;
                            case 1:
                                SetCellValue(cell, result.NodeInfo.Profile.Name ?? string.Empty);
                                break;
                            case 2:
                                SetCellValue(cell, result.NodeInfo.Profile.TestInfo ?? string.Empty);
                                break;
                            case 3:
                                SetCellValue(cell, result.NodeInfo.Profile.LabArea ?? string.Empty);
                                break;
                            case 4:
                                SetCellValue(cell, result.NodeInfo.Profile.LabName ?? string.Empty);
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
            cell.SetCellValue(dateTimeValue);
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