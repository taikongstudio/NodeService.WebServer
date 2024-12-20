﻿using NodeService.WebServer.Extensions;
using System.Linq;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class TaskExecutionInstanceListSpecification : Specification<TaskExecutionInstanceModel>
{
    public TaskExecutionInstanceListSpecification(
        string? keywords,
        TaskExecutionStatus status,
        IEnumerable<string> nodeIdList,
        IEnumerable<string> taskDefinitionIdList,
        IEnumerable<string> taskExecutionIdList,
        IEnumerable<SortDescription>? sortDescriptions = null,
        bool includeNodeInfo = false)
    {
        Query.AsSplitQuery();
        if (status != TaskExecutionStatus.Unknown) Query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (nodeIdList != null && nodeIdList.Any()) Query.Where(x => nodeIdList.Contains(x.NodeInfoId));
        if (taskDefinitionIdList != null && taskDefinitionIdList.Any())
            Query.Where(x => taskDefinitionIdList.Contains(x.TaskDefinitionId));
        if (taskExecutionIdList != null && taskExecutionIdList.Any())
            Query.Where(x => taskExecutionIdList.Contains(x.Id));
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public TaskExecutionInstanceListSpecification(
        string? keywords,
        TaskExecutionStatus status,
        IEnumerable<string> nodeIdList,
        IEnumerable<string> taskDefinitionIdList,
        IEnumerable<string> taskExecutionIdInstanceList,
        IEnumerable<string> fireInstanceIdList,
        DateTime? startTime,
        DateTime? endTime,
        IEnumerable<SortDescription>? sortDescriptions = null,
        bool includeNodeInfo = false)
    {
        Query.AsSplitQuery();
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

    public TaskExecutionInstanceListSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        DataFilterCollection<string> parentInstanceIdFilters,
        DataFilterCollection<string> childTaskDefinitionIdFilters,
        bool includeNodeInfo = false
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

    public TaskExecutionInstanceListSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        bool includeChildTasks,
        bool includeNodeInfo = false
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

    public TaskExecutionInstanceListSpecification(
        DataFilterCollection<TaskExecutionStatus> statusFilters,
        DataFilterCollection<string> nodeIdFilters,
        DataFilterCollection<string> taskDefinitionIdFilters,
        DataFilterCollection<string> taskExecutionInstanceIdFilters,
        bool includeNodeInfo = false)
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
            if (taskDefinitionIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => taskDefinitionIdFilters.Items.Contains(x.TaskDefinitionId));
            else if (taskDefinitionIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !taskDefinitionIdFilters.Items.Contains(x.TaskDefinitionId));
        }

        if (taskExecutionInstanceIdFilters.HasValue)
        {
            if (taskExecutionInstanceIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => taskExecutionInstanceIdFilters.Items.Contains(x.Id));
            else if (taskExecutionInstanceIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !taskExecutionInstanceIdFilters.Items.Contains(x.Id));
        }

    }

    public TaskExecutionInstanceListSpecification(
        DateTime dateTime,
        bool includeNodeInfo = false
)
    {
        Query.AsSplitQuery();
        Query.Where(x => x.FireTimeUtc < dateTime && x.LogEntriesSaveCount == 0);
    }

    public TaskExecutionInstanceListSpecification(
        DataFilterCollection<string> taskExecutionInstanceIdFilters,
        bool includeNodeInfo = false)
        : this(
              default,
              default,
              default,
              taskExecutionInstanceIdFilters,
              includeNodeInfo)
    {

    }
}