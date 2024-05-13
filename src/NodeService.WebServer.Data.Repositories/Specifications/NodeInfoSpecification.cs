using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeInfoSpecification : Specification<NodeInfoModel>
    {

        public NodeInfoSpecification(
            string name,
            string ipAddress)
        {
            Query.AsSplitQuery();
            Query.Where(x => x.Name == name)
                    .Where(x => x.Profile.IpAddress == ipAddress);
        }

        public NodeInfoSpecification(
            string? areaTag,
            NodeStatus nodeStatus,
            IEnumerable<SortDescription> sortDescriptions)
        {
            Query.AsSplitQuery();
            if (!string.IsNullOrEmpty(areaTag) && areaTag != AreaTags.Any)
            {
                Query.Where(x => x.Profile.FactoryName == areaTag);
            }
            if (nodeStatus != NodeStatus.All)
            {
                Query.Where(x => x.Status == nodeStatus);
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions, static (name) =>
                {
                    return name switch
                    {
                        nameof(NodeInfoModel.Name) or nameof(NodeInfoModel.Status) => name,
                        _ => $"{nameof(NodeInfoModel.Profile)}.{name}",
                    };
                });
            }
        }

        public NodeInfoSpecification(
            string? areaTag,
            NodeStatus nodeStatus,
            string? keywords,
            IEnumerable<SortDescription> sortDescriptions)
            :
            this(
                areaTag,
                nodeStatus,
                sortDescriptions)
        {

            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x =>
                 x.Name.Contains(keywords) ||
                 x.Profile.IpAddress.Contains(keywords) ||
                 x.Profile.ClientVersion.Contains(keywords) ||
                 x.Profile.IpAddress.Contains(keywords) ||
                 x.Profile.Usages.Contains(keywords) ||
                 x.Profile.Remarks.Contains(keywords));
            }

        }

        public NodeInfoSpecification(
            string? areaTag,
            NodeStatus nodeStatus,
            IEnumerable<SortDescription> sortDescriptions,
            DataFilterCollection<string> keyFilters = default,
            DataFilterCollection<string> nameFilters = default
            )
            :
            this(
                areaTag,
                nodeStatus,
                sortDescriptions)
        {
            if (keyFilters.HasValue)
            {
                Query.Where(x => keyFilters.Items.Contains(x.Id));
            }
            if (nameFilters.HasValue)
            {
                Query.Where(x => keyFilters.Items.Contains(x.Id));
            }
        }


    }
}
