﻿using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public class KafkaDelayMessageQueueService:BackgroundService
    {
        ILogger<KafkaDelayMessageQueueService> _logger;
        readonly IDelayMessageBroadcast _delayMessageBroadcast;
        readonly ExceptionCounter _exceptionCounter;
        readonly KafkaOptions _kafkaOptions;
        readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
        readonly ConsumerConfig _consumerConfig;
        readonly ProducerConfig _producerConfig;

        public KafkaDelayMessageQueueService(
            ExceptionCounter exceptionCounter,
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
            };

            _producerConfig = new ProducerConfig
            {
                BootstrapServers = _kafkaOptions.BrokerList,
                Acks = Acks.All,
                SocketTimeoutMs = 60000,
                LingerMs = 20,
            };
        }

        protected override async Task ExecuteAsync(CancellationToken  cancellationToken)
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
                            var result = await producer.ProduceAsync(_kafkaOptions.TaskDelayQueueMessageTopic, new Message<string, string>()
                            {
                                Key = kafkaDelayMessage.Type,
                                Value = JsonSerializer.Serialize(kafkaDelayMessage)
                            }, cancellationToken);

                            if (result.Status == PersistenceStatus.Persisted)
                            {
                                await _delayMessageQueue.DeuqueAsync(cancellationToken);
                            }
                        }

                        var consumeResults = await ConsumeAsync(consumer, 10000, TimeSpan.FromSeconds(3));
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
                    if (DateTime.UtcNow > delayMessage.CreateDateTime + delayMessage.Duration)
                    {
                        delayMessage.CreateDateTime = DateTime.UtcNow;
                        await _delayMessageBroadcast.BroadcastAsync(
                            delayMessage,
                            cancellationToken);
                        if (delayMessage.Handled)
                        {
                            return;
                        }
                        if (DateTime.UtcNow < delayMessage.ScheduleDateTime)
                        {
                            produceMessage = true;
                        }
                    }
                }
                else if (DateTime.UtcNow > delayMessage.ScheduleDateTime)
                {
                    await _delayMessageBroadcast.BroadcastAsync(
                        delayMessage,
                        cancellationToken);
                    if (delayMessage.Handled)
                    {
                        return;
                    }
                }
                else
                {
                    produceMessage = true;
                }
                if (produceMessage)
                {
                LRetry:
                    try
                    {
                        await consumeContext.Producer.ProduceAsync(
                            _kafkaOptions.TaskDelayQueueMessageTopic,
                            result.Message,
                            cancellationToken);
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

        Task<ImmutableArray<ConsumeResult<string, string>>> ConsumeAsync(IConsumer<string, string> consumer, int count, TimeSpan timeout)
        {
            return Task.Run(() =>
            {

                var contextsBuilder = ImmutableArray.CreateBuilder<ConsumeResult<string, string>>();
                try
                {
                    int nullCount = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var timeStamp = Stopwatch.GetTimestamp();
                        var result = consumer.Consume(timeout);
                        var consumeTimeSpan = Stopwatch.GetElapsedTime(timeStamp);
                        timeout -= consumeTimeSpan;
                        if (timeout <= TimeSpan.Zero)
                        {
                            break;
                        }
                        if (result == null)
                        {
                            if (nullCount == 3)
                            {
                                break;
                            }
                            nullCount++;
                            continue;
                        }
                        contextsBuilder.Add(result);
                        _logger.LogInformation($"Recieved {result.TopicPartitionOffset}");
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

                return contextsBuilder.ToImmutable();
            });
        }
    }
}