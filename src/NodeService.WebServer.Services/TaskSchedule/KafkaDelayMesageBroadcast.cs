using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule
{
    internal class KafkaDelayMesageBroadcast : IDelayMessageBroadcast
    {
        class CallHandlerContext
        {
            public Func<KafkaDelayMessage, CancellationToken, ValueTask> Function { get; init; }

            public KafkaDelayMessage DelayMessage { get; init; }
        }

        readonly ILogger<KafkaDelayMesageBroadcast> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly Dictionary<string, List<Func<KafkaDelayMessage, CancellationToken, ValueTask>>> _handlers;

        public KafkaDelayMesageBroadcast(
            ILogger<KafkaDelayMesageBroadcast> logger,
            ExceptionCounter exceptionCounter)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _handlers = [];
        }

        public void AddHandler(string type, Func<KafkaDelayMessage, CancellationToken, ValueTask> action)
        {
            lock (_handlers)
            {
                if (!_handlers.TryGetValue(type, out var handlerList))
                {
                    handlerList = new List<Func<KafkaDelayMessage, CancellationToken, ValueTask>>();
                    _handlers.Add(type, handlerList);
                }
                lock (handlerList)
                {
                    handlerList.Add(action);
                }
            }
        }

        public async ValueTask BroadcastAsync(
            KafkaDelayMessage kafkaDelayMessage,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(kafkaDelayMessage);

            List<CallHandlerContext> callHandlerContextList = [];
            lock (_handlers)
            {
                if (!_handlers.TryGetValue(kafkaDelayMessage.Type, out var handlerList))
                {
                    return;
                }
                lock (handlerList)
                {
                    foreach (var handler in handlerList)
                    {
                        callHandlerContextList.Add(new CallHandlerContext()
                        {
                            Function = handler,
                            DelayMessage = kafkaDelayMessage
                        });
                    }
                }
            }

            await Parallel.ForEachAsync(callHandlerContextList, new ParallelOptions()
            {
                CancellationToken = cancellationToken,
            }, ProcessHandlerContext);
        }

        async ValueTask ProcessHandlerContext(
            CallHandlerContext callHandlerContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await callHandlerContext.Function(callHandlerContext.DelayMessage, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
        }

        public void RemoveHandler(string type)
        {
            lock (_handlers)
            {
                _handlers.Remove(type);
            }
        }
    }
}
