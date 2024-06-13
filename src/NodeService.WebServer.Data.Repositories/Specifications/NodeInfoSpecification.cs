using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class NodeInfoSpecification : Specification<NodeInfoModel>
{

    static string PathSelector(string name)
    {
        return name switch
        {
            nameof(NodeInfoModel.Name) or nameof(NodeInfoModel.Status) => name,
            _ => $"{nameof(NodeInfoModel.Profile)}.{name}"
        };
    }

    public NodeInfoSpecification(
        string? name,
        string? ipAddress)
    {
        Query.AsSplitQuery();
        Query.Where(x => x.Name == name || x.Profile.IpAddress == ipAddress);
        Query.OrderByDescending(x => x.Profile.ServerUpdateTimeUtc);
    }

    public NodeInfoSpecification(
        string? areaTag,
        NodeStatus nodeStatus,
        IEnumerable<SortDescription>? sortDescriptions)
    {
        Query.AsSplitQuery();
        Query.Where(x => !x.Deleted);
        if (!string.IsNullOrEmpty(areaTag) && areaTag != AreaTags.Any)
            Query.Where(x => x.Profile.FactoryName == areaTag);
        if (nodeStatus != NodeStatus.All) Query.Where(x => x.Status == nodeStatus);
        if (sortDescriptions != null && sortDescriptions.Any())
            Query.SortBy(sortDescriptions, PathSelector);
    }

    public NodeInfoSpecification(
        string? areaTag,
        NodeStatus nodeStatus,
        string? keywords,
        bool searchProfileProperties = true,
        IEnumerable<SortDescription>? sortDescriptions = null)
        :
        this(
            areaTag,
            nodeStatus,
            sortDescriptions)
    {
        if (!string.IsNullOrEmpty(keywords))
        {
            if (!searchProfileProperties)
            {
                Query.Where(x =>
                    x.Name.Contains(keywords));
            }
            else
            {
                Query.Where(x =>
                    x.Name.Contains(keywords) ||
                    x.Profile != null && (
                  (x.Profile.IpAddress != null && x.Profile.IpAddress.Contains(keywords)) ||
                  (x.Profile.ClientVersion != null && x.Profile.ClientVersion.Contains(keywords)) ||
                  (x.Profile.IpAddress != null && x.Profile.IpAddress.Contains(keywords)) ||
                  (x.Profile.Usages != null && x.Profile.Usages.Contains(keywords)) ||
                  (x.Profile.Remarks != null && x.Profile.Remarks.Contains(keywords)))
                    );
            }
        }
    }

    public NodeInfoSpecification(
        string? areaTag,
        NodeStatus nodeStatus,
        DataFilterCollection<string> keyFilters = default,
        DataFilterCollection<string> nameFilters = default,
        IEnumerable<SortDescription>? sortDescriptions = default
    )
        :
        this(
            areaTag,
            nodeStatus,
            sortDescriptions)
    {
        if (keyFilters.HasValue)
        {
            if (keyFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => keyFilters.Items.Contains(x.Id));
            else if (keyFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !keyFilters.Items.Contains(x.Id));
        }

        if (nameFilters.HasValue)
        {
            if (nameFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nameFilters.Items.Contains(x.Name));
            else if (keyFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nameFilters.Items.Contains(x.Name));
        }
    }
}