using Microsoft.Extensions.DependencyInjection;

namespace NodeService.WebServer.Services.Tasks
{
    public class FireTaskJob : JobBase
    {


        public FireTaskJob()
        {

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
                if (TriggerSource == JobTriggerSource.Manual)
                {
                    await AsyncDispoable.DisposeAsync();
                }
            }
        }

        private async Task ExecuteCoreAsync(IJobExecutionContext context)
        {
            Logger.LogInformation($"Job fire instance id:{context.FireInstanceId}");

            var jobFireParameters = new JobFireParameters()
            {
                JobScheduleConfig = Properties["JobScheduleConfig"] as JobScheduleConfigModel,
                FireInstanceId = $"{Guid.NewGuid()}_{context.FireInstanceId}",
                FireTimeUtc = context.FireTimeUtc,
                NextFireTimeUtc = context.NextFireTimeUtc,
                PreviousFireTimeUtc = context.PreviousFireTimeUtc,
                ScheduledFireTimeUtc = context.ScheduledFireTimeUtc,
            };
            var initializer = ServiceProvider.GetService<TaskExecutionInstanceInitializer>();
            await initializer.InitAsync(jobFireParameters);
            Logger.LogInformation($"Job fire instance id:{context.FireInstanceId} end init");
        }



    }
}
