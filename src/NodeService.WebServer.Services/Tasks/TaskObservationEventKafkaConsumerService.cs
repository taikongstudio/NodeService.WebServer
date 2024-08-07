using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.NodeSessions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Collections.Immutable;
using System.Threading;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationEventKafkaConsumerService : BackgroundService
    {
        private KafkaOptions _kafkaOptions;
        readonly WebServerCounter _webServerCounter;
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<TaskObservationEventKafkaConsumerService> _logger;
        readonly IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> _fireEventQueue;
        readonly IAsyncQueue<NotificationMessage> _notificationQueue;
        readonly ConfigurationQueryService _configurationQueryService;
        private ConsumerConfig _consumerConfig;

        public TaskObservationEventKafkaConsumerService(
            ILogger<TaskObservationEventKafkaConsumerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> fireEventQueue,
            IAsyncQueue<NotificationMessage> notificationQueue,
            ConfigurationQueryService configurationQueryService)
        {
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _webServerCounter = webServerCounter;
            _exceptionCounter = exceptionCounter;
            _logger = logger;
            _fireEventQueue = fireEventQueue;
            _notificationQueue = notificationQueue;
            _configurationQueryService = configurationQueryService;
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
                    GroupId = nameof(TaskLogKafkaConsumerService),
                    FetchMaxBytes = 1024 * 1024 * 10,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
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
            foreach (var item in events)
            {
                if (item == null)
                {
                    continue;
                }
                var status = item.Type switch
                {
                    "TaskExecutionInstanceModel" => ((TaskExecutionStatus)item.Status).ToString(),
                    "TaskFlowExecutionInstanceModel" => ((TaskFlowExecutionStatus)item.Status).ToString(),
                    _ => "Unknown"
                };
                checkResultList.Add(new TaskObservationCheckResult()
                {
                    Id = item.Id,
                    Name = item.Name,
                    Context = item.Context,
                    CreationDateTime = item.CreationDateTime.ToString(),
                    Message = item.Message,
                    Status = status,
                    Solution = GetSolution(status, item.Message)
                });
            }
            if (checkResultList.Count == 0)
            {
                return;
            }
            await ProcessTaskObservationCheckResults(checkResultList, cancellationToken);
        }

        private async ValueTask ProcessTaskObservationCheckResults(
            List<TaskObservationCheckResult> checkResultList,
            CancellationToken cancellationToken = default)
        {
            var taskObservationConfiguration = await _configurationQueryService.QueryTaskObservationConfigurationAsync(cancellationToken);

            if (taskObservationConfiguration == null)
            {
                return;
            }

            if (!TryWriteToExcel(checkResultList, out var stream) || stream == null)
            {
                return;
            }

            var emailAttachment = new EmailAttachment(
                $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.xlsx",
                "application",
                "vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                stream);

            var content = taskObservationConfiguration.Content;

           var configurationListQueryResult = await _configurationQueryService.QueryConfigurationByIdListAsync<NotificationConfigModel>(taskObservationConfiguration.Configurations.Select(static x=>x.Value!), cancellationToken);

            if (!configurationListQueryResult.HasValue)
            {
                return;
            }
            foreach (var notificationConfig in configurationListQueryResult.Items)
            {
                if (notificationConfig == null || !notificationConfig.IsEnabled) continue;
                await _notificationQueue.EnqueueAsync(
                        new NotificationMessage(new EmailContent(taskObservationConfiguration.Subject, content, [emailAttachment]),
                        notificationConfig.Value),
                    cancellationToken);
            }
        }

        string GetSolution(string status, string message)
        {
            return status switch
            {
                nameof(TaskExecutionStatus.PenddingTimeout) => "等待上位机响应超时，请检查上位机运行状态。",
                nameof(TaskExecutionStatus.Failed) => "任务执行时发生了异常，请联系开发人员。",
                nameof(TaskExecutionStatus.Cancelled) => "任务被取消执行，请联系开发人员。",
                _ => string.Empty,
            };
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
                var headers = new string[] { "任务Id", "上位机名称", "任务名称", "任务启动时间", "任务状态", "任务消息", "建议处理措施" };
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
                                SetCellValue(cell, result.Context ?? string.Empty);
                                break;
                            case 2:
                                SetCellValue(cell, result.Name ?? string.Empty);
                                break;
                            case 3:
                                SetCellValue(cell, result.CreationDateTime ?? string.Empty);
                                break;
                            case 4:
                                SetCellValue(cell, result.Status ?? string.Empty);
                                break;
                            case 5:
                                SetCellValue(cell, result.Message ?? string.Empty);
                                break;
                            case 6:
                                SetCellValue(cell, result.Solution ?? string.Empty);
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
}
