using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule.Jobs;

public class FireTaskJob : JobBase
{
    readonly ExceptionCounter _exceptionCounter;

    public FireTaskJob(
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter) : base(serviceProvider)
    {
        Logger = serviceProvider.GetService<ILogger<FireTaskJob>>();
        _exceptionCounter = exceptionCounter;
    }

    public override async Task Execute(IJobExecutionContext context)
    {
        try
        {
            Logger.LogInformation($"Task fire instance id:{context.FireInstanceId}");

            var parentTaskId = Properties[nameof(FireTaskParameters.ParentTaskExecutionInstanceId)] as string;
            var fireTaskParameters = new FireTaskParameters
            {
                TaskDefinitionId = Properties[nameof(TaskDefinitionModel.Id)] as string,
                TaskActivationRecordId = $"ScheduleTask_{Guid.NewGuid()}",
                FireTimeUtc = context.FireTimeUtc,
                NextFireTimeUtc = context.NextFireTimeUtc,
                PreviousFireTimeUtc = context.PreviousFireTimeUtc,
                ScheduledFireTimeUtc = context.ScheduledFireTimeUtc,
                ParentTaskExecutionInstanceId = Properties[nameof(FireTaskParameters.ParentTaskExecutionInstanceId)] as string
            };
            var batchQueue = ServiceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
            await batchQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters));
            Logger.LogInformation($"Task fire instance id:{context.FireInstanceId} end init");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            Logger.LogError(ex.ToString());
        }
        finally
        {
            if (TriggerSource == TriggerSource.Manual && AsyncDispoable != null)
                await AsyncDispoable.DisposeAsync();
        }
    }

}