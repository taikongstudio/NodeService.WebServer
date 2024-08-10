using NodeService.Infrastructure.NodeSessions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public readonly record struct TaskActivationRecordResult
    {
        public TaskActivationRecordResult(
            FireTaskParameters fireTaskParameters,
            TaskActivationRecordModel taskActivationRecord,
            ImmutableArray<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> instances)
        {
            FireTaskParameters = fireTaskParameters;
            TaskActivationRecord = taskActivationRecord;
            Instances = instances;
        }

        public FireTaskParameters FireTaskParameters { get; init; }

        public TaskActivationRecordModel TaskActivationRecord { get; init; }

        public ImmutableArray<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> Instances { get; init; }

    }
}
