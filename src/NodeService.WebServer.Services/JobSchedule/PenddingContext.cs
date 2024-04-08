using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.JobSchedule
{
    public class PenddingContext : IAsyncDisposable
    {
        private CancellationTokenSource _penddingCancelTokenSource;

        public PenddingContext(string id, INodeSessionService nodeSessionService)
        {
            Id = id;
            NodeServerService = nodeSessionService;
        }

        public string Id { get; }

        public INodeSessionService NodeServerService { get; private set; }

        public required NodeSessionId NodeSessionId { get; init; }

        public required JobExecutionEventRequest Event { get; init; }

        public CancellationToken CancellationToken { get; private set; }

        public JobExecutionInstanceModel JobExecutionInstance { get; init; }

        public required JobFireParameters FireParameters { get; init; }

        public async Task<bool> WaitAllJobTerminatedAsync()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                if (NodeServerService.GetNodeStatus(NodeSessionId) != NodeStatus.Online)
                {
                    return false;
                }

                var jobExecutionInstances = await NodeServerService.QueryJobExecutionInstances(NodeSessionId.NodeId
                    , new QueryJobExecutionInstancesParameters()
                    {
                        Id = FireParameters.JobScheduleConfig.Id,
                        Status = JobExecutionStatus.Running
                    }, CancellationToken);

                if (!jobExecutionInstances.Any())
                {
                    return true;
                }
                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken);
            }
            return false;
        }




        public async Task<bool> WaitAnyJobTerminatedAsync()
        {
            IEnumerable<JobExecutionInstanceModel> initialInstances = null;
            IEnumerable<JobExecutionInstanceModel> currentInstances = null;
            while (!CancellationToken.IsCancellationRequested)
            {
                var jobExecutionInstances = await NodeServerService.QueryJobExecutionInstances(NodeSessionId.NodeId
                    , new QueryJobExecutionInstancesParameters()
                    {
                        Id = FireParameters.JobScheduleConfig.Id,
                        Status = JobExecutionStatus.Running
                    }, CancellationToken);
                if (!jobExecutionInstances.Any())
                {
                    return true;
                }
                if (initialInstances == null)
                {
                    initialInstances = jobExecutionInstances;
                }
                if (jobExecutionInstances.Any(
                    x => x.Status != JobExecutionStatus.Running
                    &&
                    initialInstances.Any(
                        y => y.Id == x.Id
                        )))
                {
                    return true;
                }

                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken);
            }
            return false;
        }

        public async Task<bool> KillAllJobAsync()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                var jobExecutionInstances = await NodeServerService.QueryJobExecutionInstances(NodeSessionId.NodeId,
                     new QueryJobExecutionInstancesParameters()
                     {
                         Id = FireParameters.JobScheduleConfig.Id,
                         Status = JobExecutionStatus.Running
                     },
                     CancellationToken
                    );

                int count = jobExecutionInstances.Count();


                if (count == 0)
                {
                    return true;
                }
                foreach (var jobExecutionInstance in jobExecutionInstances)
                {
                    await NodeServerService.SendJobExecutionEventAsync(
                        NodeSessionId,
                        jobExecutionInstance.ToCancelEvent(),
                        CancellationToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken);
            }
            return false;
        }

        public ValueTask InitAsync()
        {
            _penddingCancelTokenSource = new CancellationTokenSource();
            _penddingCancelTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, FireParameters.JobScheduleConfig.PenddingLimitTimeSeconds)));
            CancellationToken = _penddingCancelTokenSource.Token;
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelAsync()
        {
            if (!this._penddingCancelTokenSource.IsCancellationRequested)
            {
                return new ValueTask(this._penddingCancelTokenSource.CancelAsync());
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
