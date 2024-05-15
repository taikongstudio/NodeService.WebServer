namespace NodeService.WebServer.Data.Repositories.Specifications;

public class PropertyBagSpecification : Specification<PropertyBag>
{
    public PropertyBagSpecification(string id)
    {
        if (!string.IsNullOrEmpty(id)) Query.Where(x => x["Id"] == id);
    }
}