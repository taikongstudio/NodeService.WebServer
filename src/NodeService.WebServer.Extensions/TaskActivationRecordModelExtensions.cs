using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskActivationRecordModelExtensions
    {
        public static TaskDefinition? GetTaskDefinition(this TaskActivationRecordModel taskActivationRecord)
        {
            if (taskActivationRecord == null
                || taskActivationRecord.Value == null
                || taskActivationRecord.Value.TaskDefinitionJson == null)
            {
                return null;
            }
            return JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.Value.TaskDefinitionJson);
        }
    }
}
