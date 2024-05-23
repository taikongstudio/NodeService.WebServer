using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Queries
{
    public record class CommonConfigBatchQueueOperationParameters
    {
        public CommonConfigBatchQueueOperationParameters(Type type, PaginationQueryParameters queryParameters)
        {
            QueryParameters = queryParameters;
            Type = type;
        }

        public CommonConfigBatchQueueOperationParameters(Type type, string id)
        {
            Id = id;
            Type = type;
        }

        public PaginationQueryParameters? QueryParameters { get; private set; }

        public string? Id { get; private set; }
        public System.Type Type { get; private set; }
    }
}
