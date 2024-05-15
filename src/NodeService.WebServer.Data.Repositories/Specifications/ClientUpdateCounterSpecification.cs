namespace NodeService.WebServer.Data.Repositories.Specifications;

public class ClientUpdateCounterSpecification : Specification<ClientUpdateCounterModel>
{
    public ClientUpdateCounterSpecification(string clientUpdateConfigId, string nodeName)
    {
        Query.Where(x => x.Id == clientUpdateConfigId && x.Name == nodeName);
    }
}