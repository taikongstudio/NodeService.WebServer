namespace NodeService.WebServer.Data.Repositories.Specifications;

public class NodeProfileListSpecification : ListSpecification<NodeProfileModel>
{
    public NodeProfileListSpecification(string name)
    {
        Query.Where(x => x.Name == name);
        Query.OrderByDescending(x => x.ServerUpdateTimeUtc);
    }
}