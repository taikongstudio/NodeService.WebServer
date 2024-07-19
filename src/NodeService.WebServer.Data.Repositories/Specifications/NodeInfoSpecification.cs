using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class NodeInfoSpecification : ListSpecification<NodeInfoModel>
{
    private static string PathSelector(string name)
    {
        return name switch
        {
            nameof(NodeInfoModel.Name) or nameof(NodeInfoModel.Status) => name,
            _ => $"{nameof(NodeInfoModel.Profile)}.{name}"
        };
    }

    public NodeInfoSpecification(DataFilterCollection<string> idFilters = default)
        : this(
              AreaTags.Any,
              NodeStatus.All,
              NodeDeviceType.All,
              idFilters,
              default,
              null)
    {

    }

    public NodeInfoSpecification(
        string? name,
        string? ipAddress,
        NodeDeviceType deviceType)
    {
        Query.AsSplitQuery();
        if (deviceType != NodeDeviceType.All) Query.Where(x => x.DeviceType == deviceType);
        Query.Where(x => !x.Deleted);
        Query.Where(x => x.Name == name || x.Profile.IpAddress == ipAddress);
        Query.OrderByDescending(x => x.Profile.ServerUpdateTimeUtc);
    }

    public NodeInfoSpecification(
        string? areaTag,
        NodeStatus nodeStatus,
        NodeDeviceType deviceType,
        IEnumerable<SortDescription>? sortDescriptions)
    {


        Query.AsSplitQuery();
        if (deviceType != NodeDeviceType.All) Query.Where(x => x.DeviceType == deviceType);
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
        NodeDeviceType deviceType,
        string? keywords,
        bool searchProfileProperties = true,
        IEnumerable<SortDescription>? sortDescriptions = null)
        :
        this(
            areaTag,
            nodeStatus,
            deviceType,
            sortDescriptions)
    {
        if (!string.IsNullOrEmpty(keywords))
        {
            const string Split = "split:";
            string[] segments = [];
            if (keywords.StartsWith(Split, StringComparison.OrdinalIgnoreCase))
            {
                keywords = keywords[Split.Length..];
                if (keywords.Contains(','))
                {
                    segments = keywords.Split(',', StringSplitOptions.RemoveEmptyEntries);
                }
            }
            if (!searchProfileProperties)
                Query.Where(x =>
                    x.Name.Contains(keywords) || segments.Contains(x.Name) || segments.Any(seg => seg.Contains(x.Name)));
            else
                Query.Where(x =>
                    x.Name.Contains(keywords) || segments.Contains(x.Name)|| segments.Any(seg => seg.Contains(x.Name)) ||
                    (x.Profile != null && (
                        (x.Profile.IpAddress != null && x.Profile.IpAddress.Contains(keywords)) ||
                        (x.Profile.ClientVersion != null && x.Profile.ClientVersion.Contains(keywords)) ||
                        (x.Profile.IpAddress != null && x.Profile.IpAddress.Contains(keywords)) ||
                        (x.Profile.Usages != null && x.Profile.Usages.Contains(keywords)) ||
                        (x.Profile.Remarks != null && x.Profile.Remarks.Contains(keywords))))
                );
        }
    }

    public NodeInfoSpecification(
        string? areaTag,
        NodeStatus nodeStatus,
        NodeDeviceType nodeDeviceType,
        DataFilterCollection<string> keyFilters = default,
        DataFilterCollection<string> nameFilters = default,
        IEnumerable<SortDescription>? sortDescriptions = default
    )
        :
        this(
            areaTag,
            nodeStatus,
            nodeDeviceType,
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