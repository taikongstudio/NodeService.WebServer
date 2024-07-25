using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.UI.Pages.TaskFlows.Designer.Models
{
    public class TaskFlowDesignModel : TaskFlowDesignModelBase
    {
        public TaskTriggerType TriggerType { get; set; }
        public List<StringEntry> CronExpressions { get; set; } = [];
        public List<TaskFlowStageDesignModel> Stages { get; set; } = [];
    }

    public class TaskFlowStageDesignModel : TaskFlowDesignModelBase
    {
        public int ExecutionTimeLimitSeconds { get; set; }
        public List<TaskFlowGroupDesignModel> Groups { get; set; } = [];
    }

    public class TaskFlowGroupDesignModel : TaskFlowDesignModelBase
    {
        public List<TaskFlowTaskDesignModel> Tasks { get; set; } = [];
    }

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
