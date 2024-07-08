namespace NodeService.WebServer.Data.Repositories.Specifications;

public class TaskLogSelectSpecification<TProjection> : SelectSpecification<TaskLogModel, TProjection>
    where TProjection : class
{
    public TaskLogSelectSpecification(string id, int pageIndex)
    {
        var key = $"{id}_{pageIndex}";
        Query.Where(x => x.Id == key && x.PageIndex == pageIndex);
        Query.OrderBy(x => x.PageIndex);
    }

    public TaskLogSelectSpecification(string id, int pageIndex, int pageCount)
    {
        Query.Where(x => x.Id.Contains(id) && x.PageIndex >= pageIndex && x.PageIndex < pageIndex + pageCount);
        Query.OrderBy(x => x.PageIndex);
    }
}