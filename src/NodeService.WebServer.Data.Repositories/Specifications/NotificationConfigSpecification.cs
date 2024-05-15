using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class NotificationConfigSpecification : Specification<NotificationConfigModel>
{
    public NotificationConfigSpecification(
        string? keywords,
        int startIndex = 0,
        int count = 0,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (startIndex >= 0 && count > 0)
        {
            Query.Skip(startIndex);
            Query.Take(count);
        }

        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }
}