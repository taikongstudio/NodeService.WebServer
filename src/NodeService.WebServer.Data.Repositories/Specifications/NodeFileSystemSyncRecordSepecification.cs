using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Extensions;
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
            NodeFileSyncStatus status,
            DataFilterCollection<string> nodeIdFilters,
            DataFilterCollection<string> syncRecordIdList,
            DateTime? beginTime,
            DateTime? endTime,
            IEnumerable<SortDescription> sortDescriptions)
        {
            if (status != NodeFileSyncStatus.Unknown)
            {
                Query.Where(x => x.Status == status);
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
            if (syncRecordIdList.HasValue)
            {
                if (syncRecordIdList.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => syncRecordIdList.Items.Contains(x.Id));
                }
                else if (syncRecordIdList.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !syncRecordIdList.Items.Contains(x.Id));
                }
            }
            if (beginTime != null && beginTime.HasValue && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime >= beginTime && x.CreationDateTime <= endTime);
            else if (beginTime == null && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime <= endTime);
            else if (beginTime != null && beginTime.HasValue && endTime == null)
                Query.Where(x => x.CreationDateTime >= beginTime);
            if (sortDescriptions != null)
            {
                Query.SortBy(sortDescriptions);
            }
        }
    }
}
