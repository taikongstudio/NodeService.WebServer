using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Confluent.Kafka.ConfigPropertyNames;

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
        private IConsumer<string, string> _consumer;

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
                    GroupId = nameof(TaskLogKafkaConsumerService),
                    FetchMaxBytes = 1024 * 1024 * 10,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                };
                using (_consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build())
                {
                    _consumer.Subscribe([_kafkaOptions.ClientUpdateLogTopic]);
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            var consumeResult = _consumer.Consume(cancellationToken);
                            var log = JsonSerializer.Deserialize<ClientUpdateLog>(consumeResult.Message.Value);
                            if (log == null)
                            {
                                continue;
                            }
                            _clientUpdateCounter.Enquque(log);
                            _consumer.Commit(consumeResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex.ToString());
                            _exceptionCounter.AddOrUpdate(ex);
                        }
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
