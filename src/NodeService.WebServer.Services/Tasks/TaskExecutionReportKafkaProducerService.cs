using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;
using static Confluent.Kafka.ConfigPropertyNames;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskExecutionReportKafkaProducerService : BackgroundService
    {
        readonly ILogger<TaskExecutionReportKafkaProducerService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly IAsyncQueue<TaskExecutionReport> _taskExecutionReportQueue;
        readonly IAsyncQueue<TaskExecutionReport> _currentSendQueue;
        readonly KafkaOptions _kafkaOptions;
        readonly WebServerCounter _webServerCounter;
        readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
        private ProducerConfig _producerConfig;

        public TaskExecutionReportKafkaProducerService(
            ILogger<TaskExecutionReportKafkaProducerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IAsyncQueue<TaskExecutionReport> taskExecutionReportBatchQueue,
             [FromKeyedServices(nameof(TaskLogKafkaProducerService))] BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskExecutionReportQueue = taskExecutionReportBatchQueue;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _webServerCounter = webServerCounter;
            _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
            _currentSendQueue = new AsyncQueue<TaskExecutionReport>();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _producerConfig = new ProducerConfig
                {
                    BootstrapServers = _kafkaOptions.BrokerList,
                    Acks = Acks.All,
                    SocketTimeoutMs = 60000,
                    LingerMs = 20,
                };
                using var producer = new ProducerBuilder<string, string>(_producerConfig).Build();

                while (!cancellationToken.IsCancellationRequested)
                {

                    try
                    {

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        await _taskExecutionReportQueue.WaitToReadAsync(cancellationToken);
                        var count = Math.Min(1000, _taskExecutionReportQueue.AvailableCount);

                        var builder = ImmutableArray.CreateBuilder<TaskExecutionReport>();

                        for (int i = 0; i < count; i++)
                        {
                            var taskExecutionReport = await _taskExecutionReportQueue.DeuqueAsync(cancellationToken);
                            builder.Add(taskExecutionReport);
                            await SendLogEntriesAsync(taskExecutionReport, cancellationToken);
                        }

                        var taskExecutionReports = builder.ToImmutable();

                        foreach (var taskExecutionReport in taskExecutionReports)
                        {
                            await _currentSendQueue.EnqueueAsync(taskExecutionReport, cancellationToken);
                        }

                        while (_currentSendQueue.TryRead(out TaskExecutionReport taskExecutionReport))
                        {
                        LRetry:
                            try
                            {
                                producer.Produce(_kafkaOptions.TaskExecutionReportTopic, new Message<string, string>()
                                {
                                    Key = taskExecutionReport.Id,
                                    Value = JsonSerializer.Serialize(taskExecutionReport)
                                }, OnDeliveryReport);
                            }
                            catch (ProduceException<string, string> ex)
                            {
                                _exceptionCounter.AddOrUpdate(ex);
                                _logger.LogError(ex.ToString());
                                if (ex.DeliveryResult.Status != PersistenceStatus.Persisted)
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                                    goto LRetry;
                                }
                            }
                            catch (Exception ex)
                            {
                                _exceptionCounter.AddOrUpdate(ex);
                                _logger.LogError(ex.ToString());
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
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }

        void OnDeliveryReport(DeliveryReport<string, string> deliveryReport)
        {
            if (deliveryReport.Status == PersistenceStatus.Persisted)
            {
                _webServerCounter.TaskExecutionReportProducePersistedCount.Value++;
                var partionOffsetValue = _webServerCounter.TaskExecutionReportProducePartitionOffsetDictionary.GetOrAdd(deliveryReport.Partition.Value, new PartitionOffsetValue());
                partionOffsetValue.Partition.Value = deliveryReport.Partition.Value;
                partionOffsetValue.Offset.Value = deliveryReport.Offset.Value;
            }
            else if (deliveryReport.Status == PersistenceStatus.NotPersisted)
            {
                _webServerCounter.TaskExecutionReportProduceNotPersistedCount.Value++;
                var report = JsonSerializer.Deserialize<TaskExecutionReport>(deliveryReport.Message.Value)!;
                _currentSendQueue.TryWrite(report);
            }
        }

        private async ValueTask SendLogEntriesAsync(
            TaskExecutionReport taskExecutionReport,
            CancellationToken cancellationToken = default)
        {
            if (taskExecutionReport.LogEntries.Count > 0)
            {
                var taskLogUnit = new TaskLogUnit
                {
                    Id = taskExecutionReport.Id,
                    LogEntries = taskExecutionReport.LogEntries.Select(Convert).ToArray()
                };
                await _taskLogUnitBatchQueue.SendAsync(taskLogUnit, cancellationToken);
                _logger.LogInformation($"Send task log unit:{taskExecutionReport.Id},{taskExecutionReport.LogEntries.Count} enties");
                _webServerCounter.TaskLogUnitRecieveCount.Value++;
                taskExecutionReport.LogEntries.Clear();
            }
        }

        private static LogEntry Convert(TaskExecutionLogEntry taskExecutionLogEntry)
        {
            return new LogEntry
            {
                DateTimeUtc = taskExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
                Type = (int)taskExecutionLogEntry.Type,
                Value = taskExecutionLogEntry.Value
            };
        }

    }
}
