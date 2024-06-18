using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.QueryOptimize;

public record class FileRecordBatchQueryParameters
{
    public FileRecordBatchQueryParameters(
        QueryFileRecordListParameters queryParameters,
        PaginationInfo paginationInfo)
    {
        QueryParameters = queryParameters;
        PaginationInfo = paginationInfo;
    }

    public QueryFileRecordListParameters QueryParameters { get; private set; }

    public PaginationInfo PaginationInfo { get; private set; }
}