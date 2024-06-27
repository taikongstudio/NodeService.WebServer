using NodeService.Infrastructure;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.UI.Extensions;

public static class NodeListExtensions
{
    public static async Task<List<NodeInfoModel>> QueryNodeListAsync(this IEnumerable<StringEntry> stringEntries,
        ApiService apiService)
    {
        if (stringEntries == null || !stringEntries.Any()) return [];
        var nodeList = new List<NodeInfoModel>();
        var queryPageIndex = 0;
        var queryPageSize = 50;

        foreach (var idList in stringEntries.Chunk(50))
        {
            queryPageIndex++;
            var rsp = await apiService.QueryNodeListAsync(new QueryNodeListParameters
            {
                AreaTag = "*",
                Status = NodeStatus.All,
                IdList = idList.Select(x => x.Value).ToList(),
                PageIndex = queryPageIndex,
                PageSize = queryPageSize
            });

            if (rsp.ErrorCode == 0 && rsp.Result != null)
            {
                nodeList.AddRange(rsp.Result);
            }
        }
        return nodeList;
    }

    public static async Task<List<TaskDefinitionModel>> QueryTaskDefinitionListAsync(this IEnumerable<StringEntry> stringEntries,
    ApiService apiService)
    {
        if (stringEntries == null || !stringEntries.Any()) return [];
        var nodeList = new List<TaskDefinitionModel>();
        foreach (var id in stringEntries.Select(x => x.Value))
        {
            var rsp = await apiService.QueryTaskDefinitionAsync(id);

            if (rsp.ErrorCode == 0 && rsp.Result != null)
            {
                nodeList.Add(rsp.Result);
            }
        }
        return nodeList;
    }
}