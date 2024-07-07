using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class FireTaskFlowJob : JobBase
    {
        readonly ExceptionCounter _exceptionCounter;

        public FireTaskFlowJob(
            IServiceProvider serviceProvider,
            ExceptionCounter exceptionCounter) : base(serviceProvider)
        {
            Logger = ServiceProvider.GetService<ILogger<FireTaskFlowJob>>();
            _exceptionCounter = exceptionCounter;
        }

        public override async Task Execute(IJobExecutionContext context)
        {
            try
            {
                Logger.LogInformation($"Task fire instance id:{context.FireInstanceId}");

                var batchQueue = ServiceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
                var taskFlowInstanceId = $"Manual_TaskFlow_{Guid.NewGuid()}";
                var fireInstanceId = $"Manual_{Guid.NewGuid()}";
                await batchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskFlowParameters
                {
                    TaskFlowTemplateId = Properties[nameof(TaskFlowTemplateModel.Id)] as string,
                    FireTimeUtc = DateTime.UtcNow,
                    TriggerSource = TriggerSource.Manual,
                    TaskFlowParentInstanceId = null,
                    TaskFlowInstanceId = taskFlowInstanceId,
                    ScheduledFireTimeUtc = DateTime.UtcNow,
                }));
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
}
