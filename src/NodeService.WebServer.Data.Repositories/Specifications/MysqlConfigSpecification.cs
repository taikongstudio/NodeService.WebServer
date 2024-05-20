﻿using NodeService.WebServer.Extensions;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class MysqlConfigSpecification : Specification<DatabaseConfigModel>
{
    public MysqlConfigSpecification(
        string? keywords,
        IEnumerable<SortDescription>? sortDescriptions = null)
    {
        if (!string.IsNullOrEmpty(keywords)) Query.Where(x => x.Name.Contains(keywords));
        if (sortDescriptions != null && sortDescriptions.Any()) Query.SortBy(sortDescriptions);
    }
}