using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Confluent.Kafka.ConfigPropertyNames;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogKafkaConsumerService : BackgroundService
    {
        private readonly ILogger<TaskLogKafkaConsumerService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        KafkaOptions _kafkaOptions;
        private BatchQueue<TaskLogUnit> _taskLogUnitQueue;
        private ConsumerConfig _consumerConfig;
        private IConsumer<string, string> _consumer;

        public TaskLogKafkaConsumerService(
            ILogger<TaskLogKafkaConsumerService> logger,
            ExceptionCounter exceptionCounter,
            IOptionsMonitor<KafkaOptions> kafkaOptionsMonitor,
            [FromKeyedServices(nameof(TaskLogPersistenceService))]BatchQueue<TaskLogUnit> taskLogUnitQueue)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _kafkaOptions = kafkaOptionsMonitor.CurrentValue;
            _taskLogUnitQueue = taskLogUnitQueue;
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
                    EnableAutoCommit = true,// (the default)
                    EnableAutoOffsetStore = false,
                    GroupId = nameof(TaskLogKafkaConsumerService),
                    FetchMaxBytes = 1024 * 1024 * 10
                };
                using (_consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build())
                {
                    _consumer.Subscribe([_kafkaOptions.TaskLogTopic]);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            while (_taskLogUnitQueue.QueueCount > 10)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                            }
                            var consumeResult = _consumer.Consume(cancellationToken);
                            try
                            {
                                var taskLogUnit = JsonSerializer.Deserialize<TaskLogUnit>(consumeResult.Message.Value);
                                if (taskLogUnit != null)
                                {
                                    await _taskLogUnitQueue.SendAsync(taskLogUnit, cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _exceptionCounter.AddOrUpdate(ex);
                                _logger.LogError(ex.ToString());
                            }

                            _consumer.StoreOffset(consumeResult);
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

                    }

                    _consumer.Close();
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
