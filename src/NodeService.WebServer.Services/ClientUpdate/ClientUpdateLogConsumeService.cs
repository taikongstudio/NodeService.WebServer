using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;

namespace NodeService.WebServer.Services.ClientUpdate
{
    public class ClientUpdateLogConsumeService : BackgroundService
    {
        private readonly ILogger<ClientUpdateLogConsumeService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly KafkaOptions _kafkaOptions;
        private readonly WebServerCounter _webServerCounter;
        private readonly ClientUpdateCounter _clientUpdateCounter;
        private ConsumerConfig _consumerConfig;

        public ClientUpdateLogConsumeService(
    ILogger<ClientUpdateLogConsumeService> logger,
    ExceptionCounter exceptionCounter,
    WebServerCounter webServerCounter,
    ClientUpdateCounter clientUpdateCounter,
    IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _webServerCounter = webServerCounter;
            _clientUpdateCounter = clientUpdateCounter;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await ConsumeTaskExecutionLogAsync(cancellationToken);
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
                    GroupId = Debugger.IsAttached ? $"{nameof(ClientUpdateLogConsumeService)}_Debug" : nameof(ClientUpdateLogConsumeService),
                    FetchMaxBytes = 1024 * 1024 * 10,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    MaxPollIntervalMs = 60000 * 30,
                    HeartbeatIntervalMs = 12000,
                    SessionTimeoutMs = 45000,
                    GroupInstanceId = nameof(ClientUpdateLogConsumeService) + "GroupInstance",
                };
                using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
                consumer.Subscribe([_kafkaOptions.ClientUpdateLogTopic]);
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResults = await consumer.ConsumeAsync(10000, TimeSpan.FromSeconds(10));
                        if (consumeResults.IsDefaultOrEmpty)
                        {
                            continue;
                        }
                        foreach (var consumeResult in consumeResults)
                        {
                            var log = JsonSerializer.Deserialize<ClientUpdateLog>(consumeResult.Message.Value);
                            if (log == null)
                            {
                                continue;
                            }
                            _clientUpdateCounter.Enquque(log);
                        }

                        consumer.Commit(consumeResults.Select(static x => x.TopicPartitionOffset));

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.ToString());
                        _exceptionCounter.AddOrUpdate(ex);
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }


        }
    }
}
