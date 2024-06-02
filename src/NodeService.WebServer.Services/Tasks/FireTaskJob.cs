using Microsoft.Extensions.DependencyInjection;

namespace NodeService.WebServer.Services.Tasks;

public class FireTaskJob : JobBase
{
    public FireTaskJob(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        Logger = serviceProvider.GetService<ILogger<FireTaskJob>>();
    }

    public override async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await ExecuteCoreAsync(context);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex.ToString());
        }
        finally
        {
            if (TriggerSource == TaskTriggerSource.Manual && AsyncDispoable != null)
                await AsyncDispoable.DisposeAsync();
        }
    }

    private async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        Logger.LogInformation($"Task fire instance id:{context.FireInstanceId}");

        var parentTaskId = Properties[nameof(FireTaskParameters.ParentTaskId)] as string;
        var fireTaskParameters = new FireTaskParameters
        {
            TaskDefinitionId = Properties[nameof(ModelBase.Id)] as string,
            FireInstanceId = $"{Guid.NewGuid()}_{parentTaskId}_{context.FireInstanceId}",
            FireTimeUtc = context.FireTimeUtc,
            NextFireTimeUtc = context.NextFireTimeUtc,
            PreviousFireTimeUtc = context.PreviousFireTimeUtc,
            ScheduledFireTimeUtc = context.ScheduledFireTimeUtc,
            ParentTaskId = Properties[nameof(FireTaskParameters.ParentTaskId)] as string
        };
        var batchQueue = ServiceProvider.GetService<BatchQueue<FireTaskParameters>>();
        await batchQueue.SendAsync(fireTaskParameters);
        Logger.LogInformation($"Task fire instance id:{context.FireInstanceId} end init");
    }
}