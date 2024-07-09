namespace NodeService.WebServer.Data.Repositories.Specifications;

public class TaskLogSelectSpecification<TProjection> : SelectSpecification<TaskLogModel, TProjection>
    where TProjection : class
{

    public TaskLogSelectSpecification(string id, int pageIndex = 0)
    {
        Query.Where(x => x.TaskExecutionInstanceId == id && x.PageIndex == pageIndex);
        Query.OrderBy(x => x.PageIndex);
    }

    public TaskLogSelectSpecification(string id, int pageIndex, int count)
    {
        Query.Where(x => x.TaskExecutionInstanceId == id && x.PageIndex >= pageIndex && x.PageIndex < pageIndex + count);
        Query.OrderBy(x => x.PageIndex);
    }
}