using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationEventKafkaConsumerService : BackgroundService
    {
        KafkaOptions _kafkaOptions;
        readonly WebServerCounter _webServerCounter;
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<TaskObservationEventKafkaConsumerService> _logger;
        readonly IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> _fireEventQueue;
        readonly IAsyncQueue<NotificationMessage> _notificationQueue;
        readonly ConfigurationQueryService _configurationQueryService;
        readonly NodeInfoQueryService _nodeInfoQueryService;
        ConsumerConfig _consumerConfig;
        NodeSettings _nodeSettings;
        TaskObservationConfiguration _taskObservationConfiguration;

        public TaskObservationEventKafkaConsumerService(
            ILogger<TaskObservationEventKafkaConsumerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> fireEventQueue,
            IAsyncQueue<NotificationMessage> notificationQueue,
            ConfigurationQueryService configurationQueryService,
            NodeInfoQueryService nodeInfoQueryService)
        {
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _webServerCounter = webServerCounter;
            _exceptionCounter = exceptionCounter;
            _logger = logger;
            _fireEventQueue = fireEventQueue;
            _notificationQueue = notificationQueue;
            _configurationQueryService = configurationQueryService;
            _nodeInfoQueryService = nodeInfoQueryService;
            _nodeSettings = new NodeSettings();
        }

        protected override async Task ExecuteAsync(CancellationToken  cancellationToken=default)
        {
            try
            {
                await Task.Yield();
                _consumerConfig = new ConsumerConfig
                {
                    BootstrapServers = _kafkaOptions.BrokerList,
                    Acks = Acks.All,
                    SocketTimeoutMs = 60000,
                    EnableAutoCommit = false,// (the default)
                    EnableAutoOffsetStore = false,
                    GroupId = Debugger.IsAttached ? $"{nameof(TaskObservationEventKafkaConsumerService)}_Debug" : nameof(TaskObservationEventKafkaConsumerService),
                    FetchMaxBytes = 1024 * 1024 * 10,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    MaxPollIntervalMs = 60000 * 30,
                    HeartbeatIntervalMs = 12000,
                    SessionTimeoutMs = 45000,
                    GroupInstanceId = nameof(TaskObservationEventKafkaConsumerService) + "GroupInstance",
                };
                using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
                consumer.Subscribe([_kafkaOptions.TaskObservationEventTopic]);


                await foreach (var _ in _fireEventQueue.ReadAllAsync(cancellationToken))
                {
                    var elapsed = TimeSpan.Zero;
                    var consumeResults = ImmutableArray<ConsumeResult<string, string>>.Empty;
                    try
                    {
                        var timeStamp = Stopwatch.GetTimestamp();
                        consumeResults = await consumer.ConsumeAsync(10000, TimeSpan.FromMinutes(1));
                        if (consumeResults.IsDefaultOrEmpty)
                        {
                            continue;
                        }

                        var events = consumeResults.Select(static x => JsonSerializer.Deserialize<TaskObservationEvent>(x.Message.Value)!).ToImmutableArray();


                        await ProcessTaskObservationEventsAsync(
                            events,
                            cancellationToken);

                        consumer.Commit(consumeResults.Select(static x => x.TopicPartitionOffset));

                        foreach (var result in consumeResults)
                        {
                            var partionOffsetValue = _webServerCounter.Snapshot.KafkaTaskObservationEventConsumePartitionOffsetDictionary.GetOrAdd(result.Partition.Value, PartitionOffsetValue.CreateNew);
                            partionOffsetValue.Partition.Value = result.Partition.Value;
                            partionOffsetValue.Offset.Value = result.Offset.Value;
                            partionOffsetValue.Message = result.Message.Value;
                            _webServerCounter.Snapshot.TaskObservationEventConsumeCount.Value++;
                        }

                        elapsed = Stopwatch.GetElapsedTime(timeStamp);

                    }
                    catch (ConsumeException ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                    finally
                    {

                    }
                }


            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }

        private async ValueTask ProcessTaskObservationEventsAsync(ImmutableArray<TaskObservationEvent> events, CancellationToken cancellationToken)
        {
            events = events.Distinct().ToImmutableArray();
            List<TaskObservationCheckResult> checkResultList = [];
            foreach (var eventItem in events)
            {
                if (eventItem == null)
                {
                    continue;
                }
                var status = eventItem.Type switch
                {
                    "TaskExecutionInstanceModel" => ((TaskExecutionStatus)eventItem.Status).GetDisplayName(),
                    "TaskFlowExecutionInstanceModel" => ((TaskFlowExecutionStatus)eventItem.Status).GetDisplayName(),
                    _ => "Unknown"
                };

                NodeInfoModel? nodeInfo = null;
                if (eventItem.Type == "TaskExecutionInstanceModel")
                {
                    nodeInfo = await _nodeInfoQueryService.QueryNodeInfoByIdAsync(
                        eventItem.Context,
                        true,
                        cancellationToken);
                }
                var checkResult = new TaskObservationCheckResult()
                {
                    Id = eventItem.Id,
                    Name = eventItem.Name,
                    NodeInfo = nodeInfo,
                    CreationDateTime = eventItem.CreationDateTime.ToString(),
                    Message = eventItem.Message,
                    Status = status
                };
                if (nodeInfo != null)
                {
                    var computerInfo = await _nodeInfoQueryService.Query_dl_equipment_ctrl_computer_Async(
                        nodeInfo.Id,
                        nodeInfo.Profile.LimsDataId,
                        cancellationToken);
                    if (computerInfo != null)
                    {
                        if (computerInfo.IsScrapped())
                        {
                            continue;
                        }
                        checkResult.DisplayName = computerInfo.name;
                    }
                }
                checkResultList.Add(checkResult);
            }
            if (checkResultList.Count == 0)
            {
                return;
            }
            await ProcessTaskObservationCheckResultsAsync(checkResultList, cancellationToken);
        }

        private async ValueTask ProcessTaskObservationCheckResultsAsync(
            List<TaskObservationCheckResult> checkResultList,
            CancellationToken cancellationToken = default)
        {
            _taskObservationConfiguration = await _configurationQueryService.QueryTaskObservationConfigurationAsync(cancellationToken);

            if (_taskObservationConfiguration == null)
            {
                return;
            }

            var notificationConfigQueryResult = await _configurationQueryService.QueryConfigurationByIdListAsync<NotificationConfigModel>(
                _taskObservationConfiguration.Configurations.Where(static x => x.Value != null)
                .Select(static x => x.Value!)
                .ToImmutableArray(),
                cancellationToken);

            var notificationConfigList = notificationConfigQueryResult.Items.ToImmutableArray();

            if (notificationConfigList.IsDefaultOrEmpty)
            {
                return;
            }

            _nodeSettings = await _configurationQueryService.QueryNodeSettingsAsync(cancellationToken);

            if (_nodeSettings == null)
            {
                return;
            }

            var dataDict = checkResultList.GroupBy(static x => x.NodeInfo?.Profile.FactoryName ?? AreaTags.Any).ToDictionary(static x => x.Key);

            foreach (var notificationConfig in notificationConfigList)
            {
                if (!notificationConfig.Value.IsEnabled)
                {
                    continue;
                }
                if (notificationConfig.Value.FactoryName == AreaTags.Any || notificationConfig.Value.FactoryName == null)
                {

                    await SendEmailAsync(
                        checkResultList,
                        "全部区域",
                        notificationConfig,
                        cancellationToken);
                }
                else
                {
                    if (!dataDict.TryGetValue(notificationConfig.Value.FactoryName, out var group) || group == null || !group.Any())
                    {
                        continue;
                    }
                    string factoryName = group.Key;
                    var areaEntry = _nodeSettings.IpAddressMappings.FirstOrDefault(x => x.Tag == group.Key);
                    if (areaEntry != null && areaEntry.Name != null)
                    {
                        factoryName = areaEntry.Name;
                    }
                    await SendEmailAsync(
                        group,
                        factoryName,
                        notificationConfig,
                        cancellationToken);
                }

            }

        }

        async ValueTask SendEmailAsync(
            IEnumerable<TaskObservationCheckResult> checkResults,
            string factoryName,
            NotificationConfigModel notificationConfig,
            CancellationToken cancellationToken)
        {
            if (_taskObservationConfiguration == null)
            {
                return;
            }

            var subject = _taskObservationConfiguration.Subject
                .Replace("$(FactoryName)", factoryName)
                .Replace("$(DateTime)", DateTime.Now.ToString(EmailContent.DateTimeFormat));

            List<XlsxAttachment> attachments = [];
            foreach (var testInfoGroup in checkResults.GroupBy(static x => x.NodeInfo?.Profile.TestInfo ?? string.Empty))
            {
                var bizType = testInfoGroup.Key;
                if (string.IsNullOrEmpty(bizType))
                {
                    bizType = "未分类";
                }

                var sendEmail = false;
                if (notificationConfig.Value.Tags != null)
                {
                    foreach (var tag in notificationConfig.Value.Tags)
                    {
                        if (tag.Value == bizType)
                        {
                            sendEmail = true;
                            break;
                        }
                    }
                }
                if (!sendEmail)
                {
                    continue;
                }

                foreach (var item in testInfoGroup)
                {
                    foreach (var messsageTemplate in _taskObservationConfiguration.MessageTemplates)
                    {
                        if (string.IsNullOrEmpty(messsageTemplate.Name))
                        {
                            continue;
                        }
                        if (item.Message != null && item.Message.Contains(messsageTemplate.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Solution = messsageTemplate.Value;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(item.Solution))
                    {
                        var defaultTemplate = _taskObservationConfiguration.MessageTemplates.FirstOrDefault(x => x.Name == "*");
                        if (defaultTemplate != null)
                        {
                            item.Solution = defaultTemplate.Value;
                        }
                        else
                        {
                            item.Solution = "<请配置此消息的模板文本>";
                        }
                    }
                }
                if (!TryWriteToExcel([.. testInfoGroup], out var stream) || stream == null)
                {
                    continue;
                }
                var attachmentName = _taskObservationConfiguration.AttachmentSubject
                    .Replace("$(FactoryName)", factoryName)
                    .Replace("$(DateTime)", DateTime.Now.ToString(EmailContent.DateTimeFormat))
                    .Replace("$(BusinessType)", bizType);
                var fileName = $"{attachmentName}.xlsx";
                var emailAttachment = new XlsxAttachment(
                    fileName,
                    stream);
                attachments.Add(emailAttachment);
            }
            if (attachments.Count == 0)
            {
                return;
            }
            var content = _taskObservationConfiguration.Content.Replace("$(FactoryName)", factoryName)
                    .Replace("$(DateTime)", DateTime.Now.ToString(EmailContent.DateTimeFormat));
            var emailContent = new EmailContent(
                subject,
                content,
                [.. attachments]);
            await _notificationQueue.EnqueueAsync(
                new NotificationMessage(emailContent, notificationConfig.Value),
                cancellationToken);
        }

        public bool TryWriteToExcel(List<TaskObservationCheckResult> checkResults, out Stream? stream)
        {
            stream = null;
            try
            {
                //创建工作薄  
                IWorkbook wb = new XSSFWorkbook();

                //创建一个表单
                ISheet sheet = wb.CreateSheet("任务监控");
                //设置列宽
                int[] columnWidth = { 20, 20, 20, 20, 20, 20, 20, 50, 50 };
                for (int i = 0; i < columnWidth.Length; i++)
                {
                    //设置列宽度，256*字符数，因为单位是1/256个字符
                    sheet.SetColumnWidth(i, 256 * columnWidth[i]);
                }

                //测试数据

                IRow headerRow = sheet.CreateRow(0);
                var headers = new string[]
                {
                    "任务Id",
                    "上位机名称",
                    "主机名",
                    "业务类型",
                    "实验室名称",
                    "测试区域",
                    "上位机负责人",
                    "IP地址",
                    "任务名称",
                    "任务启动时间",
                    "任务状态",
                    "任务消息",
                    "建议处理措施"
                };
                {
                    for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    {
                        var cell = headerRow.CreateCell(columnIndex);//创建第j列
                        SetCellValue<string>(cell, headers[columnIndex]);
                    }
                }


                var rowIndex = 1;
                for (int dataIndex = 0; dataIndex < checkResults.Count; dataIndex++)
                {
                    var result = checkResults[dataIndex];

                    var dataRow = sheet.CreateRow(rowIndex);
                    rowIndex++;
                    for (int columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                    {
                        var cell = dataRow.CreateCell(columnIndex);
                        switch (columnIndex)
                        {
                            case 0:
                                SetCellValue(cell, result.Id);
                                break;
                            case 1:
                                SetCellValue(cell, result.DisplayName ?? string.Empty);
                                break;
                            case 2:
                                SetCellValue(cell, result.NodeInfo?.Profile.Name ?? string.Empty);
                                break;
                            case 3:
                                SetCellValue(cell, result.NodeInfo?.Profile.TestInfo ?? string.Empty);
                                break;
                            case 4:
                                SetCellValue(cell, result.NodeInfo?.Profile.LabName ?? string.Empty);
                                break;
                            case 5:
                                SetCellValue(cell, result.NodeInfo?.Profile.LabArea ?? string.Empty);
                                break;
                            case 6:
                                SetCellValue(cell, result.NodeInfo?.Profile.Manager ?? string.Empty);
                                break;
                            case 7:
                                SetCellValue(cell, result.NodeInfo?.Profile.IpAddress ?? string.Empty);
                                break;
                            case 8:
                                SetCellValue(cell, result.Name);
                                break;
                            case 9:
                                SetCellValue(cell, result.CreationDateTime);
                                break;
                            case 10:
                                SetCellValue(cell, result.Status);
                                break;
                            case 11:
                                SetCellValue(cell, result.Message);
                                break;
                            case 12:
                                SetCellValue(cell, result.Solution);
                                break;
                            default:
                                break;
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
}
