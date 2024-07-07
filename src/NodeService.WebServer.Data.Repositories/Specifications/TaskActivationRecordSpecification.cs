using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskActivationRecordSpecification : Specification<TaskActivationRecordModel>
    {
        public TaskActivationRecordSpecification(
            string? keywords,
            TaskExecutionStatus status,
            DateTime? startTime,
            DateTime? endTime,
            DataFilterCollection<string> idFilters = default,
            DataFilterCollection<string> taskDefinitionFilters = default,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (status != TaskExecutionStatus.Unknown)
            {
                Query.Where(x => x.Status == status);
            }
            if (startTime == null && endTime != null)
            {
                Query.Where(x => x.CreationDateTime <= endTime);
            }
            else if (startTime != null && endTime == null)
            {
                Query.Where(x => x.CreationDateTime >= startTime);
            }
            else if (startTime != null && endTime != null)
            {
                Query.Where(x => x.CreationDateTime >= startTime && x.CreationDateTime <= endTime);
            }

            if (idFilters.HasValue)
            {
                if (idFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => idFilters.Items.Contains(x.Id));
                }
                else if (idFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !idFilters.Items.Contains(x.Id));
                }
            }
            if (taskDefinitionFilters.HasValue)
            {
                if (taskDefinitionFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => taskDefinitionFilters.Items.Contains(x.Id));
                }
                else if (taskDefinitionFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !taskDefinitionFilters.Items.Contains(x.Id));
                }
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

        public TaskActivationRecordSpecification(DataFilterCollection<string> idFilters)
        {
            if (idFilters.HasValue)
            {
                if (idFilters.FilterType== DataFilterTypes.Include)
                {
                    Query.Where(x => idFilters.Items.Contains(x.Id));
                }
                else if (idFilters.FilterType== DataFilterTypes.Exclude)
                {
                    Query.Where(x =>! idFilters.Items.Contains(x.Id));
                }
            }
        }
    }
}
