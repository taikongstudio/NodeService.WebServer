using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class CommonConfigurationSpecification<T> : Specification<T> where T : JsonBasedDataModel
{
    public CommonConfigurationSpecification(
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public CommonConfigurationSpecification(
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