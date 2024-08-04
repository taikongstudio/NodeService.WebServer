using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskExecutionReportKafkaProducerService : BackgroundService
    {
        readonly ILogger<TaskExecutionReportKafkaProducerService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly IAsyncQueue<TaskExecutionReport> _taskExecutionReportQueue;
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
                LRetry:
                    try
                    {
                        if (!_taskExecutionReportQueue.TryPeek(out TaskExecutionReport taskExecutionReport) || taskExecutionReport == null)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                            continue;
                        }
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

                        var result = await producer.ProduceAsync(_kafkaOptions.TaskExecutionReportTopic, new Message<string, string>()
                        {
                            Key = taskExecutionReport.Id,
                            Value = JsonSerializer.Serialize(taskExecutionReport)
                        }, cancellationToken);

                        if (result.Status == PersistenceStatus.Persisted)
                        {
                            await _taskExecutionReportQueue.DeuqueAsync(cancellationToken);
                            _webServerCounter.TaskExecutionReportProduceRetryCount.Value++;
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }

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
