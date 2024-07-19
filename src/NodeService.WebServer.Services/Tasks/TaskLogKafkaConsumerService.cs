using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogKafkaConsumerService : BackgroundService
    {
        class ConsumeContext
        {
            public ConsumeResult<string, string> Result { get; init; }

            public int Index { get; init; }

            public TaskLogUnit? Unit { get; set; }
        }

        class ConsumeContextGroup
        {
            public string? Id { get; set; }

            public IEnumerable<ConsumeContext>? Contexts { get; init; }

            public TimeSpan ProcessTimeSpan { get; set; }
        }

        private readonly ILogger<TaskLogKafkaConsumerService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        KafkaOptions _kafkaOptions;
        private BatchQueue<AsyncOperation<TaskLogUnit[]>> _taskLogUnitQueue;
        private readonly WebServerCounter _webServerCounter;
        private ConsumerConfig _consumerConfig;
        private IConsumer<string, string> _consumer;

        private List<ConsumeContext> _nullList;
        private TimeSpan _timeSpanConsume;
        private TimeSpan _timeSpanDeserialize;
        private TimeSpan _timeSpanProcess;
        private TimeSpan _timeSpanCommit;
        private TimeSpan _maxConsumeContextGroupProcessTime;
        private double _scaleFactor;

        public TaskLogKafkaConsumerService(
            ILogger<TaskLogKafkaConsumerService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            [FromKeyedServices(nameof(TaskLogPersistenceService))] BatchQueue<AsyncOperation<TaskLogUnit[]>> taskLogUnitQueue)
        {
            _logger = logger;
            _nullList = [null];
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _taskLogUnitQueue = taskLogUnitQueue;
            _webServerCounter = webServerCounter;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await Task.WhenAll(ConsumeTaskExecutionLogAsync(cancellationToken), Task.CompletedTask);
        }

        private async Task ConsumeTaskExecutionLogAsync(CancellationToken cancellationToken = default)
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
                using (_consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build())
                {
                    _consumer.Subscribe([_kafkaOptions.TaskLogTopic]);
                    ImmutableArray<ConsumeContext> prefecthList = [];
                    _scaleFactor = 1d;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {

                            _timeSpanDeserialize = TimeSpan.Zero;
                            _timeSpanProcess = TimeSpan.Zero;
                            _timeSpanCommit = TimeSpan.Zero;

                            var contexts = !prefecthList.IsDefaultOrEmpty ? prefecthList : await ConsumeAsync((int)(100 * _scaleFactor), 500);

                            if (contexts.IsDefaultOrEmpty)
                            {
                                continue;
                            }
                            prefecthList = [];

                            var stopwatch = Stopwatch.StartNew();
                            await Parallel.ForEachAsync(contexts, DeserializeAsync);

                            stopwatch.Stop();
                            _timeSpanDeserialize = stopwatch.Elapsed;

                            stopwatch.Restart();

                            var consumeContextGroups = contexts.GroupBy(GroupConsumeContext)
                                                               .Select(CreateConsumeContextGroup)
                                                               .ToArray();

                            var forEachConsumeTask = Parallel.ForEachAsync(
                                consumeContextGroups,
                                ProcessConsumeContextGroupAsync);

                            var pefetchTask = ConsumeAsync((int)(100 * _scaleFactor), 500);

                            _webServerCounter.KafkaTaskLogConsumeTotalTimeSpan.Value += _timeSpanConsume;
                            if (_timeSpanConsume > _webServerCounter.KafkaTaskLogConsumeMaxTimeSpan.Value)
                            {
                                _webServerCounter.KafkaTaskLogConsumeMaxTimeSpan.Value = _timeSpanConsume;
                            }

                            await Task.WhenAll(pefetchTask, forEachConsumeTask);
                            prefecthList = pefetchTask.Result;

                            _webServerCounter.KafkaTaskLogConsumePrefetchCount.Value = prefecthList.Length;
                            if (prefecthList.Length > _webServerCounter.KafkaTaskLogConsumeMaxPrefetchCount.Value)
                            {
                                _webServerCounter.KafkaTaskLogConsumeMaxPrefetchCount.Value = prefecthList.Length;
                            }

                            _maxConsumeContextGroupProcessTime = consumeContextGroups.Max(x => x.ProcessTimeSpan);



                            if (_maxConsumeContextGroupProcessTime > _webServerCounter.KafkaTaskLogConsumeContextGroupMaxTimeSpan.Value)
                            {
                                _webServerCounter.KafkaTaskLogConsumeContextGroupMaxTimeSpan.Value = _maxConsumeContextGroupProcessTime;
                            }
                            _webServerCounter.KafkaTaskLogConsumeContextGroupAvgTimeSpan.Value = TimeSpan.FromMicroseconds(consumeContextGroups.Average(x => x.ProcessTimeSpan.TotalMicroseconds));

                           _scaleFactor = Math.Max(_maxConsumeContextGroupProcessTime / _timeSpanConsume, 1);

                            if (prefecthList.IsDefaultOrEmpty)
                            {
                                _scaleFactor = 1;
                            }

                            if (_scaleFactor > 100)
                            {
                                _scaleFactor = 100;
                            }

                            _webServerCounter.KafkaTaskLogConsumeScaleFactor.Value = TimeSpan.FromSeconds(_scaleFactor);

                            stopwatch.Stop();

                            _timeSpanProcess = stopwatch.Elapsed;

                            stopwatch.Restart();

                            _consumer.Commit(contexts.Select(static x => x.Result.TopicPartitionOffset));
                            _webServerCounter.KafkaTaskLogConsumeCount.Value += contexts.Length;

                            foreach (var item in contexts)
                            {
                                var value = _webServerCounter.ConsumePartitionOffsetDictionary.GetOrAdd(item.Result.Partition.Value, new PartitionOffsetValue()
                                {

                                });
                                value.Partition.Value = item.Result.Partition.Value;
                                value.Offset.Value = item.Result.Offset.Value;
                            }




                            stopwatch.Stop();
                            _timeSpanCommit = stopwatch.Elapsed;

                        }
                        catch (ConsumeException ex)
                        {
                            _exceptionCounter.AddOrUpdate(ex);
                            _logger.LogError(ex.ToString());
                        }
                        catch (KafkaException ex)
                        {
                            _exceptionCounter.AddOrUpdate(ex);
                            _logger.LogError(ex.ToString());
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

                    _consumer.Close();
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }




            Task<ImmutableArray<ConsumeContext>> ConsumeAsync(int count, int timeout)
            {
                return Task.Run<ImmutableArray<ConsumeContext>>(() =>
                {
                    _timeSpanConsume = TimeSpan.Zero;
                    var timeStamp = Stopwatch.GetTimestamp();
                    var contextsBuilder = ImmutableArray.CreateBuilder<ConsumeContext>();
                    try
                    {
                        int nullCount = 0;
                        for (int i = 0; i < count; i++)
                        {
                            var result = _consumer.Consume(timeout);
                            if (result == null)
                            {
                                if (nullCount == 3)
                                {
                                    break;
                                }
                                nullCount++;
                                continue;
                            }
                            contextsBuilder.Add(new ConsumeContext()
                            {
                                Index = i,
                                Result = result
                            });
                            _logger.LogInformation($"Recieved {result.TopicPartitionOffset}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                    _timeSpanConsume = Stopwatch.GetElapsedTime(timeStamp);
                    return contextsBuilder.ToImmutable();
                });
            }
        }

        static string? GroupConsumeContext(ConsumeContext consumeContext)
        {
            return consumeContext?.Unit?.Id;
        }

        static ConsumeContextGroup CreateConsumeContextGroup(IGrouping<string?, ConsumeContext>? consumeContextGroup)
        {
            return new ConsumeContextGroup()
            {
                Id = consumeContextGroup?.Key,
                Contexts = consumeContextGroup
            };
        }

        async ValueTask ProcessConsumeContextGroupAsync(
            ConsumeContextGroup consumeContextGroup,
            CancellationToken cancellationToken = default)
        {
            var timestamp = Stopwatch.GetTimestamp();
            if (consumeContextGroup.Id == null)
            {
                return;
            }
            var taskLogUnits = consumeContextGroup.Contexts.Select(static x => x.Unit).ToArray();
            if (taskLogUnits == null)
            {
                return;
            }
            var asyncOperation = new AsyncOperation<TaskLogUnit[]>(taskLogUnits, AsyncOperationKind.AddOrUpdate);
            await _taskLogUnitQueue.SendAsync(asyncOperation, cancellationToken);
            await asyncOperation.WaitAsync();
            _webServerCounter.KafkaTaskLogConsumeCount.Value += taskLogUnits.Length;
            consumeContextGroup.ProcessTimeSpan = Stopwatch.GetElapsedTime(timestamp);
        }

        ValueTask DeserializeAsync(
            ConsumeContext consumeContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var taskLogUnit = JsonSerializer.Deserialize<TaskLogUnit>(consumeContext.Result.Message.Value);
                consumeContext.Unit = taskLogUnit;
                _logger.LogInformation($"Deserialize {consumeContext.Result.TopicPartitionOffset}");
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            return ValueTask.CompletedTask;
        }

    }
}
