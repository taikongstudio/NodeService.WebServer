namespace NodeService.WebServer.UI.Pages.TaskFlows.Instances.Models
{
    public class TaskFlowInstanceModel : TaskFlowInstanceModelBase
    {
        public string Id { get; set; }
    }

    public class TaskFlowStageInstanceModel : TaskFlowInstanceModelBase
    {
        public string Id { get; set; }

        public List<TaskFlowGroupInstanceModel> Groups { get; set; } = [];
    }

    public class TaskFlowGroupInstanceModel : TaskFlowInstanceModelBase
    {
        public string Id { get; set; }

        public List<TaskFlowTaskInstanceModel> Tasks { get; set; } = [];
    }

    public class TaskFlowTaskInstanceModel : TaskFlowInstanceModelBase
    {
        public string Id { get; set; }
    }
}
