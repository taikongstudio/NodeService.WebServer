namespace NodeService.WebServer.Services.Tasks
{
    public class TaskFlowExecutionTimeLimitParameters
    {
        public TaskFlowExecutionTimeLimitParameters(
            string taskFlowInstanceId,
            string taskFlowStageInstanceId)
        {
            TaskFlowInstanceId = taskFlowInstanceId;
            TaskFlowStageInstanceId = taskFlowStageInstanceId;
        }

        public string TaskFlowInstanceId { get; set; }

        public string TaskFlowStageInstanceId { get; set; }
    }
}
