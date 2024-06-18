using NodeService.WebServer.Extensions;
using System.Linq;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class TaskExecutionInstanceSpecification : Specification<TaskExecutionInstanceModel>
{
    public TaskExecutionInstanceSpecification(
        string? keywords,
        TaskExecutionStatus status,
        IEnumerable<string> nodeIdList,
        IEnumerable<string> taskDefinitionIdList,
        IEnumerable<string> taskExecutionIdList,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (status != TaskExecutionStatus.Unknown) Query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (nodeIdList != null && nodeIdList.Any()) Query.Where(x => nodeIdList.Contains(x.NodeInfoId));
        if (taskDefinitionIdList != null && taskDefinitionIdList.Any())
            Query.Where(x => taskDefinitionIdList.Contains(x.JobScheduleConfigId));
        if (taskExecutionIdList != null && taskExecutionIdList.Any())
            Query.Where(x => taskExecutionIdList.Contains(x.Id));
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public TaskExecutionInstanceSpecification(
        string? keywords,
        TaskExecutionStatus status,
        IEnumerable<string> nodeIdList,
        IEnumerable<string> taskDefinitionIdList,
        IEnumerable<string> taskExecutionIdInstanceList,
        DateTime? startTime,
        DateTime? endTime,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (status != TaskExecutionStatus.Unknown) Query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (nodeIdList != null && nodeIdList.Any()) Query.Where(x => nodeIdList.Contains(x.NodeInfoId));
        if (taskDefinitionIdList != null && taskDefinitionIdList.Any())
            Query.Where(x => taskDefinitionIdList.Contains(x.JobScheduleConfigId));
        if (taskExecutionIdInstanceList != null && taskExecutionIdInstanceList.Any())
            Query.Where(x => taskExecutionIdInstanceList.Contains(x.Id));
        if (startTime != null && startTime.HasValue && endTime != null && endTime.HasValue)
            Query.Where(x => x.FireTimeUtc >= startTime && x.FireTimeUtc <= endTime);
        else if (startTime == null && endTime != null && endTime.HasValue)
            Query.Where(x => x.FireTimeUtc <= endTime);
        else if (startTime != null && startTime.HasValue && endTime == null)
            Query.Where(x => x.FireTimeUtc >= startTime);

        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public TaskExecutionInstanceSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        DataFilterCollection<string> nodeIdFilters,
        DataFilterCollection<string> taskDefinitionIdFilters,
        DataFilterCollection<string> taskExecutionInstanceIdFilters)
    {
        if (statusFilters.HasValue)
        {
            if (statusFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => statusFilters.Items.Contains(x.Status));
            else if (statusFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !statusFilters.Items.Contains(x.Status));
        }

        if (nodeIdFilters.HasValue)
        {
            if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nodeIdFilters.Items.Contains(x.NodeInfoId));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.NodeInfoId));
        }

        if (taskDefinitionIdFilters.HasValue)
        {
            if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nodeIdFilters.Items.Contains(x.JobScheduleConfigId));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.JobScheduleConfigId));
        }

        if (taskExecutionInstanceIdFilters.HasValue)
        {
            if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nodeIdFilters.Items.Contains(x.Id));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.Id));
        }
    }
}