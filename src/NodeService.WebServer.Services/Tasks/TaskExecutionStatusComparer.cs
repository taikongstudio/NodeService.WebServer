namespace NodeService.WebServer.Services.Tasks;

internal partial class TaskActivationRecordProcessContext
{
    class TaskExecutionStatusComparer : IComparer<TaskExecutionStatus>
    {
        public int Compare(TaskExecutionStatus x, TaskExecutionStatus y)
        {
            if (x == TaskExecutionStatus.PenddingCancel && y == TaskExecutionStatus.Cancelled)
            {
                return -1;
            }
            else if (x == TaskExecutionStatus.Cancelled && y == TaskExecutionStatus.PenddingCancel)
            {
                return 1;
            }
            return x - y;
        }
    }

}