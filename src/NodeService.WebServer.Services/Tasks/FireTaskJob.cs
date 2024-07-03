using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Tasks;

public class FireTaskJob : JobBase
{
    readonly ExceptionCounter _exceptionCounter;

    public FireTaskJob(IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter) : base(serviceProvider)
    {
        Logger = serviceProvider.GetService<ILogger<FireTaskJob>>();
        _exceptionCounter = exceptionCounter;
    }

    public override async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await ExecuteCoreAsync(context);
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

    private async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        Logger.LogInformation($"Task fire instance id:{context.FireInstanceId}");

        var parentTaskId = Properties[nameof(FireTaskParameters.ParentTaskId)] as string;
        var fireTaskParameters = new FireTaskParameters
        {
            TaskDefinitionId = Properties[nameof(TaskDefinitionModel.Id)] as string,
            FireInstanceId = $"ScheduleTask_{Guid.NewGuid()}",
            FireTimeUtc = context.FireTimeUtc,
            NextFireTimeUtc = context.NextFireTimeUtc,
            PreviousFireTimeUtc = context.PreviousFireTimeUtc,
            ScheduledFireTimeUtc = context.ScheduledFireTimeUtc,
            ParentTaskId = Properties[nameof(FireTaskParameters.ParentTaskId)] as string
        };
        var batchQueue = ServiceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
        await batchQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters));
        Logger.LogInformation($"Task fire instance id:{context.FireInstanceId} end init");
    }
}