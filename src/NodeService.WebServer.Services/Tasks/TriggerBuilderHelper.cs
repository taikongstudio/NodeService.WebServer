using System.Collections.ObjectModel;

namespace NodeService.WebServer.Services.Tasks
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
            return new([TriggerBuilder.Create().StartNow().Build()]);
        }

        public static ReadOnlyCollection<ITrigger> BuildDelayTrigger(TimeSpan timeSpan)
        {
            return new([TriggerBuilder.Create().StartAt(DateTime.UtcNow.ToUniversalTime().Add(timeSpan)).Build()]);
        }

    }
}
