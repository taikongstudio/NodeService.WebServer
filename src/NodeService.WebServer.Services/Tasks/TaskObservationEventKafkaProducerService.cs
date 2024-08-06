using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationEventKafkaProducerService : BackgroundService
    {
        readonly IAsyncQueue<TaskObservationEvent> _eventQueue;
        readonly ILogger<TaskObservationEventKafkaProducerService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly WebServerCounter _webServerCounter;
        readonly KafkaOptions _kafkaOptions;
        private ProducerConfig _producerConfig;

        public TaskObservationEventKafkaProducerService(
            ILogger<TaskObservationEventKafkaProducerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IAsyncQueue<TaskObservationEvent> eventQueue,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor)
        {
            _eventQueue = eventQueue;
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _webServerCounter = webServerCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
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
                        if (!_eventQueue.TryPeek(out TaskObservationEvent taskObservationEvent) || taskObservationEvent == null)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                            continue;
                        }

                        var result = await producer.ProduceAsync(_kafkaOptions.TaskObservationEventTopic, new Message<string, string>()
                        {
                            Key = taskObservationEvent.Id,
                            Value = JsonSerializer.Serialize(taskObservationEvent)
                        }, cancellationToken);

                        if (result.Status == PersistenceStatus.Persisted)
                        {
                            await _eventQueue.DeuqueAsync(cancellationToken);
                            _webServerCounter.TaskObservationProduceRetryCount.Value++;
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
    }
}
