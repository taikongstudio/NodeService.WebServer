using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class FileRecordSpecification : Specification<FileRecordModel>
{
    public FileRecordSpecification(
        string nodeId,
        string? category,
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(nodeId)) Query.Where(x => x.Id == nodeId);
        if (!string.IsNullOrEmpty(category)) Query.Where(x => x.Category == category);
        if (!string.IsNullOrEmpty(keywords))
            Query.Where(x => x.OriginalFileName.Contains(keywords) || x.Name == keywords);
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public FileRecordSpecification(
        DataFilterCollection<string> nameFilters = default,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (nameFilters.HasValue)
        {
            if (nameFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nameFilters.Items.Contains(x.Name));
            else if (nameFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => !nameFilters.Items.Contains(x.Name));
        }

        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public FileRecordSpecification(
        string? category,
        FileRecordState state,
        DataFilterCollection<string> idFilters = default,
        DataFilterCollection<string> nameFilters = default,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (state != FileRecordState.None) Query.Where(x => x.State == state);
        if (!string.IsNullOrEmpty(category)) Query.Where(x => x.Category == category);
        if (idFilters.HasValue)
        {
            if (idFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => idFilters.Items.Contains(x.Id));
            else if (idFilters.FilterType == DataFilterTypes.Exclude) Query.Where(x => !idFilters.Items.Contains(x.Id));
        }

        if (nameFilters.HasValue)
            foreach (var item in nameFilters.Items)
                if (nameFilters.FilterType == DataFilterTypes.Include)
                    Query.Where(x => x.OriginalFileName.Contains(item) || x.Name == item);
                else if (nameFilters.FilterType == DataFilterTypes.Exclude)
                    Query.Where(x => !x.OriginalFileName.Contains(item) && x.Name != item);

        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }
}