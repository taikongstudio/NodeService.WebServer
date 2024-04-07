using CronExpressionDescriptor;
using Google.Protobuf.WellKnownTypes;
using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.JobSchedule
{
    public static class TriggerBuilderHelper
    {
        public static ReadOnlyCollection<ITrigger> BuildScheduleTrigger(IEnumerable<string> cronExpressions)
        {
            return cronExpressions
                .Where(CheckCronExpression)
                .Select(ProcessCronExpression)
                .Select(x => TriggerBuilder.Create()
                                .WithCronSchedule(x.Trim())
                                .Build())
                                .ToList()
                                .AsReadOnly();
        }

        private static bool CheckCronExpression(string value)
        {
            return CronExpression.IsValidExpression(value);
        }

        private static string ProcessCronExpression(string value)
        {
            value = value.Trim();
            value = value.Trim('\r');
            value = value.Trim('\n');
            value = value.Trim('\t');
            return value;
        }

        public static ReadOnlyCollection<ITrigger> BuildStartNowTrigger()
        {
            return new ReadOnlyCollection<ITrigger>(new ITrigger[] {
                     TriggerBuilder.Create().StartNow().Build()
                });
        }

        public static ReadOnlyCollection<ITrigger> BuildStartAtTrigger(TimeSpan timeSpan)
        {
            return new ReadOnlyCollection<ITrigger>(new ITrigger[] {
                     TriggerBuilder.Create().StartAt(DateTime.UtcNow.ToUniversalTime().Add(timeSpan)).Build()
                });
        }

    }
}
