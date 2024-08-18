using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataServices;
using System.Threading;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogKafkaProducerService : BackgroundService
    {
        private readonly ILogger<TaskLogKafkaProducerService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        KafkaOptions _kafkaOptions;
        private BatchQueue<TaskLogUnit> _taskLogUnitQueue;
        private WebServerCounter _webServerCounter;
        private IAsyncQueue<TaskLogUnit> _taskLogUnitAnalysisQueue;
        private ObjectCache _objectCache;
        private ProducerConfig _producerConfig;
        private IProducer<string, string> _producer;

        public TaskLogKafkaProducerService(
            ILogger<TaskLogKafkaProducerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            [FromKeyedServices(nameof(TaskLogKafkaProducerService))] BatchQueue<TaskLogUnit> taskLogUnitQueue,
            [FromKeyedServices(nameof(TaskLogAnalysisService))] IAsyncQueue<TaskLogUnit> taskLogUnitAnalysisQueue)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _taskLogUnitQueue = taskLogUnitQueue;
            _webServerCounter = webServerCounter;
            _taskLogUnitAnalysisQueue = taskLogUnitAnalysisQueue;
        }



        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            _producerConfig = new ProducerConfig
            {
                BootstrapServers = _kafkaOptions.BrokerList,
                Acks = Acks.All,
                SocketTimeoutMs = 60000,
                LingerMs = 20,
            };
            try
            {
                using (_producer = new ProducerBuilder<string, string>(_producerConfig).Build())
                {
                    await foreach (var taskLogUnits in _taskLogUnitQueue.ReceiveAllAsync(cancellationToken))
                    {
                        if (taskLogUnits == null || taskLogUnits.Length == 0)
                        {
                            continue;
                        }
                        try
                        {
                            foreach (var taskLogUnit in taskLogUnits.SelectMany(SplitTaskLogUnit))
                            {
                                if (taskLogUnit == null)
                                {
                                    continue;
                                }
                                var logString = string.Empty;
                            LRetry:
                                try
                                {
                                    if (logString == string.Empty)
                                    {
                                        logString = JsonSerializer.Serialize(taskLogUnit);
                                    }

                                    await _taskLogUnitAnalysisQueue.EnqueueAsync(taskLogUnit, cancellationToken);

                                    var length = logString.Length;
                                    _producer.Produce(_kafkaOptions.TaskLogTopic, new Message<string, string>()
                                    {
                                        Key = taskLogUnit.Id,
                                        Value = logString
                                    }, DeliveryHandler);
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
                            _producer.Flush(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _exceptionCounter.AddOrUpdate(ex);
                            _logger.LogError(ex.ToString());
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

        IEnumerable<TaskLogUnit> SplitTaskLogUnit(TaskLogUnit taskLogUnit)
        {
            foreach (var logEntries in taskLogUnit.LogEntries.Chunk(200))
            {
                yield return new TaskLogUnit()
                {
                    Id = taskLogUnit.Id,
                    LogEntries = logEntries,
                    Status = taskLogUnit.Status
                };
            }
            yield break;
        }

        void DeliveryHandler(DeliveryReport<string, string> report)
        {
            if (report.Status == PersistenceStatus.Persisted)
            {
                _webServerCounter.Snapshot.KafkaTaskLogProduceCount.Value++;
                var partionOffsetValue = _webServerCounter.Snapshot.KafkaLogProducePartitionOffsetDictionary.GetOrAdd(report.Partition.Value, PartitionOffsetValue.CreateNew);
                partionOffsetValue.Partition.Value = report.Partition.Value;
                partionOffsetValue.Offset.Value = report.Offset.Value;
                partionOffsetValue.Message = report.Message.Value;
            }
            else
            {
                _webServerCounter.Snapshot.KafkaTaskLogProduceRetryCount.Value++;
            }
        }
    }
}
