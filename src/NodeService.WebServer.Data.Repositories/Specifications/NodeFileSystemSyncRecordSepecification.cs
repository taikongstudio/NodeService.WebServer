using NodeService.Infrastructure.NodeFileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeFileSystemSyncRecordSepecification : SelectSpecification<NodeFileSyncRecordModel, string>
    {
        public NodeFileSystemSyncRecordSepecification(
            NodeFileSyncStatus nodeFileSyncStatus,
            DataFilterCollection<string> nodeIdFilters,
            DataFilterCollection<string> nodeFileSyncRecordIdList,
            DateTime? beginTime,
            DateTime? endTime,
            IEnumerable<SortDescription> sortDescriptions)
        {
            if (nodeFileSyncStatus != NodeFileSyncStatus.Unknown)
            {
                Query.Where(x => x.Status == NodeFileSyncStatus.Unknown);
            }
            if (nodeIdFilters.HasValue)
            {
                if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => nodeIdFilters.Items.Contains(x.NodeInfoId));
                }
                else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !nodeIdFilters.Items.Contains(x.NodeInfoId));
                }
            }
            if (nodeFileSyncRecordIdList.HasValue)
            {
                if (nodeFileSyncRecordIdList.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => nodeFileSyncRecordIdList.Items.Contains(x.Id));
                }
                else if (nodeFileSyncRecordIdList.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !nodeFileSyncRecordIdList.Items.Contains(x.Id));
                }
            }
            if (beginTime != null && beginTime.HasValue && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime >= beginTime && x.CreationDateTime <= endTime);
            else if (beginTime == null && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime <= endTime);
            else if (beginTime != null && beginTime.HasValue && endTime == null)
                Query.Where(x => x.CreationDateTime >= beginTime);
        }
    }
}
