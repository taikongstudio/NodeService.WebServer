using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public class KafkaDelayMessageQueueService : BackgroundService
    {
        ILogger<KafkaDelayMessageQueueService> _logger;
        readonly IDelayMessageBroadcast _delayMessageBroadcast;
        readonly ExceptionCounter _exceptionCounter;
        readonly KafkaOptions _kafkaOptions;
        readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
        readonly ConsumerConfig _consumerConfig;
        readonly ProducerConfig _producerConfig;
        private readonly WebServerCounter _webServerCounter;

        public KafkaDelayMessageQueueService(
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            ILogger<KafkaDelayMessageQueueService> logger,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            IDelayMessageBroadcast delayMessageBroadcast,
            IAsyncQueue<KafkaDelayMessage> delayMessageQueue)
        {
            _logger = logger;
            _delayMessageBroadcast = delayMessageBroadcast;
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _delayMessageQueue = delayMessageQueue;
            _consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _kafkaOptions.BrokerList,
                Acks = Acks.All,
                SocketTimeoutMs = 60000,
                EnableAutoCommit = false,// (the default)
                EnableAutoOffsetStore = false,
                GroupId = nameof(KafkaDelayMessageQueueService),
                FetchMaxBytes = 1024 * 1024 * 10,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                MaxPollIntervalMs = 60000 * 30,
                HeartbeatIntervalMs = 12000,
                SessionTimeoutMs = 45000,
                GroupInstanceId = nameof(KafkaDelayMessageQueueService) + "GroupInstance",
            };

            _producerConfig = new ProducerConfig
            {
                BootstrapServers = _kafkaOptions.BrokerList,
                Acks = Acks.All,
                SocketTimeoutMs = 60000,
                LingerMs = 20,
            };
            _webServerCounter = webServerCounter;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
                using var producer = new ProducerBuilder<string, string>(_producerConfig).Build();

                consumer.Subscribe([_kafkaOptions.TaskDelayQueueMessageTopic]);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        while (_delayMessageQueue.TryPeek(out KafkaDelayMessage kafkaDelayMessage))
                        {
                            var deliveryReport = await producer.ProduceAsync(_kafkaOptions.TaskDelayQueueMessageTopic, new Message<string, string>()
                            {
                                Key = kafkaDelayMessage.Id,
                                Value = JsonSerializer.Serialize(kafkaDelayMessage)
                            }, cancellationToken);

                            if (deliveryReport.Status == PersistenceStatus.Persisted)
                            {
                                await _delayMessageQueue.DeuqueAsync(cancellationToken);
                                var partionOffsetValue = _webServerCounter.KafkaDelayMessageProducePartitionOffsetDictionary.GetOrAdd(deliveryReport.Partition.Value, PartitionOffsetValue.CreateNew);
                                partionOffsetValue.Partition.Value = deliveryReport.Partition.Value;
                                partionOffsetValue.Offset.Value = deliveryReport.Offset.Value;
                                partionOffsetValue.Message = deliveryReport.Message.Value;
                            }
                        }

                        var consumeResults = await consumer.ConsumeAsync(10000, TimeSpan.FromSeconds(3));
                        if (!consumeResults.IsDefaultOrEmpty)
                        {
                            var consumeContexts = consumeResults.Select(x => new KafkaDelayMessageConsumeContext()
                            {
                                Result = x,
                                Producer = producer
                            }).ToImmutableArray();

                            if (Debugger.IsAttached)
                            {
                                foreach (var consumeContext in consumeContexts)
                                {
                                    await ProcessConsumeContextAsync(consumeContext, cancellationToken);
                                }
                            }
                            else
                            {
                                await Parallel.ForEachAsync(consumeContexts, new ParallelOptions()
                                {
                                    CancellationToken = cancellationToken,
                                    MaxDegreeOfParallelism = 4
                                }, ProcessConsumeContextAsync);
                            }
                            
                            consumer.Commit(consumeResults.Select(static x => x.TopicPartitionOffset));

                            foreach (var consumeResult in consumeResults)
                            {
                                var partionOffsetValue = _webServerCounter.KafkaDelayMessageConsumePartitionOffsetDictionary.GetOrAdd(consumeResult.Partition.Value, PartitionOffsetValue.CreateNew);
                                partionOffsetValue.Partition.Value = consumeResult.Partition.Value;
                                partionOffsetValue.Offset.Value = consumeResult.Offset.Value;
                                partionOffsetValue.Message = consumeResult.Message.Value;
                            }

                            _webServerCounter.KafkaDelayMessageConsumeCount.Value += consumeContexts.Length;
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

        async ValueTask ProcessConsumeContextAsync(
            KafkaDelayMessageConsumeContext consumeContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                bool produceMessage = false;
                var result = consumeContext.Result;
                var delayMessage = JsonSerializer.Deserialize<KafkaDelayMessage>(result.Message.Value);
                if (delayMessage is null)
                {
                    return;
                }
                else if (delayMessage.Duration > TimeSpan.Zero)
                {
                    await _delayMessageBroadcast.BroadcastAsync(
                            delayMessage,
                            cancellationToken);
                    _webServerCounter.KafkaDelayMessageTickCount.Value++;
                    if (delayMessage.Handled)
                    {
                        _webServerCounter.KafkaDelayMessageHandledCount.Value++;
                        return;
                    }
                    produceMessage = true;
                    if (DateTime.UtcNow > delayMessage.CreateDateTime + delayMessage.Duration)
                    {
                        delayMessage.CreateDateTime = DateTime.UtcNow;
                    }
                }
                else if (DateTime.UtcNow > delayMessage.ScheduleDateTime)
                {

                    await _delayMessageBroadcast.BroadcastAsync(
                        delayMessage,
                        cancellationToken);

                    _webServerCounter.KafkaDelayMessageScheduleCount.Value++;

                    if (delayMessage.Handled)
                    {
                        _webServerCounter.KafkaDelayMessageHandledCount.Value++;
                        return;
                    }
                    produceMessage = true;
                }
                if (produceMessage)
                {
                LRetry:
                    try
                    {
                        var deliveryReport = await consumeContext.Producer.ProduceAsync(
                            _kafkaOptions.TaskDelayQueueMessageTopic,
                            result.Message,
                            cancellationToken);
                        if (deliveryReport.Status == PersistenceStatus.Persisted)
                        {
                            _webServerCounter.KafkaDelayMessageProduceCount.Value++;
                            var partionOffsetValue = _webServerCounter.KafkaDelayMessageProducePartitionOffsetDictionary.GetOrAdd(deliveryReport.Partition.Value, PartitionOffsetValue.CreateNew);
                            partionOffsetValue.Partition.Value = deliveryReport.Partition.Value;
                            partionOffsetValue.Offset.Value = deliveryReport.Offset.Value;
                            partionOffsetValue.Message = deliveryReport.Message.Value;
                        }
                    }
                    catch (ProduceException<string, string> ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                        goto LRetry;
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
