using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskFlowTemplateModelExtensions
    {
        public static TaskFlowTaskTemplate? GetTriggerTask(this TaskFlowTemplateModel taskFlowTemplate)
        {
            var firstStage = taskFlowTemplate.TaskStages.FirstOrDefault();
            var firstGroup = firstStage?.TaskGroups.FirstOrDefault();
            var triggerTask = firstGroup?.Tasks.FirstOrDefault();
            return triggerTask;
        }
    }
}
