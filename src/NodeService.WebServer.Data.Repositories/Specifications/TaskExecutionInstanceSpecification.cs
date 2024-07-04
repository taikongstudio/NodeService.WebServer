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
            Query.Where(x => taskDefinitionIdList.Contains(x.TaskDefinitionId));
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
        IEnumerable<string> fireInstanceIdList,
        DateTime? startTime,
        DateTime? endTime,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (status != TaskExecutionStatus.Unknown) Query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (nodeIdList != null && nodeIdList.Any()) Query.Where(x => nodeIdList.Contains(x.NodeInfoId));
        if (taskDefinitionIdList != null && taskDefinitionIdList.Any())
            Query.Where(x => taskDefinitionIdList.Contains(x.TaskDefinitionId));
        if (taskExecutionIdInstanceList != null && taskExecutionIdInstanceList.Any())
            Query.Where(x => taskExecutionIdInstanceList.Contains(x.Id));
        if (fireInstanceIdList != null && fireInstanceIdList.Any())
        {
            Query.Where(x => fireInstanceIdList.Contains(x.FireInstanceId));
        }
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
        DataFilterCollection<string> parentInstanceIdFilters,
        DataFilterCollection<string> childTaskDefinitionIdFilters
        )
    {
        if (statusFilters.HasValue)
        {
            if (statusFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => statusFilters.Items.Contains(x.Status));
            else if (statusFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !statusFilters.Items.Contains(x.Status));
        }
        if (parentInstanceIdFilters.HasValue)
        {
            if (parentInstanceIdFilters.FilterType == DataFilterTypes.Include)
            {
                Query.Where(x => parentInstanceIdFilters.Items.Contains(x.ParentId));
            }
            else if (parentInstanceIdFilters.FilterType == DataFilterTypes.Exclude)
            {
                Query.Where(x => !parentInstanceIdFilters.Items.Contains(x.ParentId));
            }
        }
        if (childTaskDefinitionIdFilters.HasValue)
        {
            if (childTaskDefinitionIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => childTaskDefinitionIdFilters.Items.Contains(x.TaskDefinitionId));
            else if (childTaskDefinitionIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !childTaskDefinitionIdFilters.Items.Contains(x.TaskDefinitionId));
        }

    }

    public TaskExecutionInstanceSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        bool includeChildTasks
    )
    {
        Query.AsSplitQuery();
        if (statusFilters.HasValue)
        {
            if (statusFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => statusFilters.Items.Contains(x.Status));
            else if (statusFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !statusFilters.Items.Contains(x.Status));
        }
        if (includeChildTasks)
        {
            Query.Where(x => x.ParentId != null);
        }
    }

    public TaskExecutionInstanceSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        DataFilterCollection<string> nodeIdFilters,
        DataFilterCollection<string> taskDefinitionIdFilters,
        DataFilterCollection<string> taskExecutionInstanceIdFilters)
    {
        Query.AsSplitQuery();
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
                Query.Where(x => nodeIdFilters.Items.Contains(x.TaskDefinitionId));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.TaskDefinitionId));
        }

        if (taskExecutionInstanceIdFilters.HasValue)
        {
            if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nodeIdFilters.Items.Contains(x.Id));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.Id));
        }

    }

    public TaskExecutionInstanceSpecification(
        DateTime dateTime
)
    {
        Query.Where(x => x.FireTimeUtc < dateTime && x.LogEntriesSaveCount > 0);
    }
}