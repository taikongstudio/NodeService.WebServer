using NodeService.Infrastructure;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Extensions
{
    public static class NodeListExtensions
    {
        public static async Task<List<NodeInfoModel>> LoadNodeListAsync(this IEnumerable<StringEntry> stringEntries, ApiService apiService)
        {
            if (stringEntries == null || !stringEntries.Any())
            {
                return [];
            }
            var nodeList = new List<NodeInfoModel>();
            var queryPageIndex = 0;
            var queryPageSize = 50;
            while (true)
            {
                var idList = stringEntries
                    .Skip(queryPageIndex * queryPageSize)
                    .Take(queryPageSize)
                    .Select(x => x.Value)
                    .ToList();
                if (idList.Count == 0)
                {
                    break;
                }

                queryPageIndex++;
                var pageIndex = 0;
                var pageSize = 50;
                var count = 0;
                while (true)
                {
                    pageIndex++;
                    var rsp = await apiService.QueryNodeListAsync(new QueryNodeListParameters
                    {
                        AreaTag = "*",
                        Status = NodeStatus.All,
                        IdList = idList,
                        PageIndex = pageIndex,
                        PageSize = pageSize
                    });

                    if (rsp.ErrorCode == 0 && rsp.Result != null)
                    {
                        nodeList.AddRange(rsp.Result);
                        count += rsp.Result.Count();
                    }

                    if (rsp.TotalCount == count)
                    {
                        break;
                    }
                }
            }


            return nodeList;
        }

    }
}
