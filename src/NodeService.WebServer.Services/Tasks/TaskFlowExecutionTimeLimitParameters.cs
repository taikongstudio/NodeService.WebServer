namespace NodeService.WebServer.Services.Tasks
{
    public class TaskFlowExecutionTimeLimitParameters
    {
        public TaskFlowExecutionTimeLimitParameters(
            string taskFlowInstanceId,
            string taskFlowStageInstanceId)
        {
            TaskFlowExecutionInstanceId = taskFlowInstanceId;
            TaskFlowStageInstanceId = taskFlowStageInstanceId;
        }

        public string TaskFlowExecutionInstanceId { get; set; }

        public string TaskFlowStageInstanceId { get; set; }
    }
}
