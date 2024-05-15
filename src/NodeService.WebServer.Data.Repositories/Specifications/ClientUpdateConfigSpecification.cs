using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class ClientUpdateConfigSpecification : Specification<ClientUpdateConfigModel>
{
    public ClientUpdateConfigSpecification(
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name == keywords);
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public ClientUpdateConfigSpecification(
        ClientUpdateStatus status,
        string name)
    {
        Query.Where(x => x.Status == status)
            .Where(x => x.Name == name)
            .OrderByDescending(static x => x.Version);
    }
}