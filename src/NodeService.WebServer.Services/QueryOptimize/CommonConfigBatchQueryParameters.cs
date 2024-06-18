using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.QueryOptimize;

public record class CommonConfigBatchQueryParameters
{
    public CommonConfigBatchQueryParameters(Type type, PaginationQueryParameters queryParameters)
    {
        QueryParameters = queryParameters;
        Type = type;
    }

    public CommonConfigBatchQueryParameters(Type type, string id)
    {
        Id = id;
        Type = type;
    }

    public PaginationQueryParameters? QueryParameters { get; private set; }

    public string? Id { get; private set; }
    public Type Type { get; private set; }
}