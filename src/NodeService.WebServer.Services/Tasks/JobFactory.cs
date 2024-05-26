using Microsoft.Extensions.DependencyInjection;
using Quartz.Spi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class JobFactory : IJobFactory
    {
        readonly IServiceProvider _serviceProvider;

        public JobFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            var job = _serviceProvider.GetService(bundle.JobDetail.JobType) as JobBase;
            job.Properties = bundle.JobDetail.JobDataMap[nameof(JobBase.Properties)] as IDictionary<string, object?>;
            job.TriggerSource = (TaskTriggerSource)bundle.JobDetail.JobDataMap[nameof(JobBase.TriggerSource)];
            job.AsyncDispoable = bundle.JobDetail.JobDataMap[nameof(JobBase.AsyncDispoable)] as IAsyncDisposable;
            return job;
        }

        public void ReturnJob(IJob job)
        {
            var disposable = job as IDisposable;
            disposable?.Dispose();
        }
    }
}
