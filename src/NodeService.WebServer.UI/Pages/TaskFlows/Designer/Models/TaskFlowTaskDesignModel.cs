using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Pages.TaskFlows.Designer.Models
{
    public class TaskFlowTaskDesignModel : TaskFlowDesignModelBase
    {
        public TaskDefinitionModel TaskDefinition { get; private set; }

        public void SetTaskDefinition(TaskDefinitionModel? taskDefinition)
        {
            if (taskDefinition == null)
            {
                this.Name = "未选择任务定义";
                this.TaskDefinition = null;
            }
            else
            {
                this.TaskDefinition = taskDefinition;
                this.Name = taskDefinition.Name;
            }
        }
    }
}
