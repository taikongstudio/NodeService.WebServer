using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskObservationEventKafkaConsumerService : BackgroundService
    {
        private KafkaOptions _kafkaOptions;
        readonly WebServerCounter _webServerCounter;
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<TaskObservationEventKafkaConsumerService> _logger;
        readonly IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> _fireEventQueue;
        private ConsumerConfig _consumerConfig;

        public TaskObservationEventKafkaConsumerService(
            ILogger<TaskObservationEventKafkaConsumerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent> fireEventQueue)
        {
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _webServerCounter = webServerCounter;
            _exceptionCounter = exceptionCounter;
            _logger = logger;
            _fireEventQueue = fireEventQueue;
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
                    TimeSpan elapsed = TimeSpan.Zero;
                    ImmutableArray<ConsumeResult<string, string>> consumeResults = [];
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

        }


    }
}
