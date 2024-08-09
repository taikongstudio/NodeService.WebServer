using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Threading;

namespace NodeService.WebServer.Services.ClientUpdate
{
    public class ClientUpdateLogProduceService : BackgroundService
    {
        private readonly ILogger<ClientUpdateLogProduceService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        KafkaOptions _kafkaOptions;
        private BatchQueue<ClientUpdateLog> _clientUpdateLogQueue;
        private WebServerCounter _webServerCounter;
        private ProducerConfig _producerConfig;
        private IProducer<string, string> _producer;

        public ClientUpdateLogProduceService(
            ILogger<ClientUpdateLogProduceService> logger,
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            [FromKeyedServices(nameof(ClientUpdateLogProduceService))] BatchQueue<ClientUpdateLog> clientUpdateLogQueue)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _clientUpdateLogQueue = clientUpdateLogQueue;
            _webServerCounter = webServerCounter;
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
                    await foreach (var clientUpdateLogs in _clientUpdateLogQueue.ReceiveAllAsync(cancellationToken))
                    {
                        if (clientUpdateLogs == null || clientUpdateLogs.Length == 0)
                        {
                            continue;
                        }
                        try
                        {
                            foreach (var clientUpdateLog in clientUpdateLogs)
                            {
                                if (clientUpdateLog == null)
                                {
                                    continue;
                                }
                                string logString = string.Empty;
                            LRetry:
                                try
                                {
                                    if (logString == string.Empty)
                                    {
                                        logString = JsonSerializer.Serialize(clientUpdateLog);
                                    }
                                    var length = logString.Length;
                                    _producer.Produce(_kafkaOptions.ClientUpdateLogTopic, new Message<string, string>()
                                    {
                                        Key = $"{clientUpdateLog.ClientUpdateConfigId}{clientUpdateLog.NodeName}",
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

        void DeliveryHandler(DeliveryReport<string, string> report)
        {
            if (report.Status != PersistenceStatus.Persisted)
            {

            }
            else
            {

                var value = _webServerCounter.KafkaLogProducePartitionOffsetDictionary.GetOrAdd(report.Partition.Value, PartitionOffsetValue.CreateNew);
                value.Partition.Value = report.Partition.Value;
                value.Offset.Value = report.Offset.Value;
            }
        }
    }
}
