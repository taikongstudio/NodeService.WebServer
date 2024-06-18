using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class CommonConfigSpecification<T> : Specification<T> where T : ModelBase
{
    public CommonConfigSpecification(
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public CommonConfigSpecification(
        DataFilterCollection<string> idFilters)
    {
        if (idFilters.HasValue)
        {
            if (idFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => idFilters.Items.Contains(x.Id));
            else if (idFilters.FilterType == DataFilterTypes.Exclude) Query.Where(x => !idFilters.Items.Contains(x.Id));
        }
    }
}