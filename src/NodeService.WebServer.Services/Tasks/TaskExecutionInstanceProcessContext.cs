using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService
{
    class TaskExecutionInstanceProcessContext
    {
        public TaskExecutionInstanceProcessContext(
            TaskExecutionInstanceModel? taskExecutionInstance,
            bool statusChanged,
            bool messageChanged)
        {
            TaskExecutionInstance = taskExecutionInstance;
            StatusChanged = statusChanged;
            MessageChanged = messageChanged;
        }

        public TaskExecutionInstanceModel? TaskExecutionInstance { get; set; }

        public bool StatusChanged { get; set; }

        public bool MessageChanged { get; set; }

        public IEnumerable<TaskExecutionReportMessage> Messages { get; init; }
    }
}