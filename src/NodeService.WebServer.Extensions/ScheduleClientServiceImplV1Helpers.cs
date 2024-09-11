using Google.Protobuf.WellKnownTypes;
using NodeService.Infrastructure.Models;
using Quartz;
using TriggerState = NodeService.Infrastructure.Models.TriggerState;
using JobKey = NodeService.Infrastructure.Models.JobKey;
using TriggerKey = NodeService.Infrastructure.Models.TriggerKey;

public static class ScheduleClientServiceImplV1Helpers
{


    public static JobDetail ToJobDetail(this IJobDetail jobDetailObject)
    {
        JobDetail jobDetail = new JobDetail()
        {
            Description = jobDetailObject.Description,
            ConcurrentExecutionDisallowed = jobDetailObject.ConcurrentExecutionDisallowed,
            Durable = jobDetailObject.Durable,
            JobType = jobDetailObject.JobType.FullName,
            Key = new JobKey()
            {
                Group = jobDetailObject.Key.Group,
                Name = jobDetailObject.Key.Name
            },
            PersistJobDataAfterExecution = jobDetailObject.PersistJobDataAfterExecution,
            RequestsRecovery = jobDetailObject.RequestsRecovery,
        };
        jobDetail.JobDataMap = new Dictionary<string, string>();
        foreach (var item in jobDetailObject.JobDataMap)
        {
            jobDetail.JobDataMap.Add(item.Key, item.Value?.ToString());
        }

        return jobDetail;
    }

    public static async ValueTask<TriggerInfo> ToTriggerInfoAsync(
        this ITrigger trigger,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        var triggerInfo = new TriggerInfo
        {
            Key = new TriggerKey()
            {
                Group = trigger.Key.Group,
                Name = trigger.Key.Name,
            },
            JobKey = new JobKey()
            {
                Group = trigger.JobKey.Group,
                Name = trigger.JobKey.Name,
            },
            Description = trigger.Description,
            CalendarName = trigger.Description,
            Priority = trigger.Priority,
            HasMillisecondPrecision = trigger.HasMillisecondPrecision,
            MayFireAgain = trigger.GetMayFireAgain(),
            FinalFireTimeUtc = trigger.FinalFireTimeUtc,
            StartTimeUtc = trigger.StartTimeUtc,
            EndTimeUtc = trigger.EndTimeUtc,
            PreviousFireTimeUtc = trigger.GetPreviousFireTimeUtc(),
            NextFireTimeUtc = trigger.GetNextFireTimeUtc(),
            MisfireInstruction = trigger.MisfireInstruction,
            TriggerState = await GetTriggerStateAsync(scheduler, trigger, cancellationToken),
        };
        foreach (var data in trigger.JobDataMap)
        {
            triggerInfo.DataMap.Add(data.Key, data.Value.ToString());
        }
        return triggerInfo;
    }

    private static async ValueTask<TriggerState> GetTriggerStateAsync(
        IScheduler scheduler,
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (TriggerState)await scheduler.GetTriggerState(trigger.Key, cancellationToken);
        }
        catch
        {

        }
        return TriggerState.TriggerState_None;
    }
}