using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class IConsumerExtensions
    {
        public static Task<ImmutableArray<ConsumeResult<TKey, TValue>>> ConsumeAsync<TKey, TValue>(
            this IConsumer<TKey, TValue> consumer,
            int count,
            TimeSpan timeout)
        {
            return Task.Run<ImmutableArray<ConsumeResult<TKey, TValue>>>(() =>
            {

                var contextsBuilder = ImmutableArray.CreateBuilder<ConsumeResult<TKey, TValue>>();
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
                }

                return contextsBuilder.ToImmutable();
            });
        }

    }
}
