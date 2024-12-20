﻿using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class NotificationRecordSpecification : ListSpecification<NotificationRecordModel>
{
    public NotificationRecordSpecification(
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        Query.OrderByDescending(x => x.CreationDateTime);
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }

    public NotificationRecordSpecification(string id)
    {
        Query.Where(x => x.Id == id);
    }
}