using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public readonly record struct JobSchedulerKey
    {
        public JobSchedulerKey(string key, JobTriggerSource triggerSource)
        {
            Key = key;
            TriggerSource = triggerSource;
        }

        public string Key { get; init; }

        public JobTriggerSource TriggerSource { get; init; }
    }

    public class JobSchedulerDictionary : ConcurrentDictionary<JobSchedulerKey, IAsyncDisposable>
    {

    }
}
