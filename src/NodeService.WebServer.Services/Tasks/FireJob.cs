using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NodeService.WebServer.Services.Tasks
{
    public class FireJob : JobBase
    {


        public FireJob()
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
            var initializer = ServiceProvider.GetService<JobExecutionInstanceInitializer>();
            await initializer.InitAsync(jobFireParameters);
        }



    }
}
