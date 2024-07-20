using Google.Protobuf.Compiler;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.DataQueue
{
    public class NodeStatusChangeQueryService
    {
        readonly IServiceProvider _serviceProvider;
        readonly IMemoryCache _memoryCache;
        readonly ObjectCache _objectCache;
        readonly ApplicationRepositoryFactory<ConfigurationVersionRecordModel> _configVersionRepoFactory;
        readonly ILogger<ConfigurationQueryService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        private readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _applicationRepositoryFactory;

        public NodeStatusChangeQueryService(
            ILogger<ConfigurationQueryService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeStatusChangeRecordModel> applicationRepositoryFactory
        )
        {
            _applicationRepositoryFactory = applicationRepositoryFactory;
        }

        public async ValueTask<ListQueryResult<NodeStatusChangeStatisticItem>> QueryAsync(
            QueryNodeStatusChangeStatisticsParameters parameters,
            CancellationToken cancellationToken = default)
        {
            await using var repo = await _applicationRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            var query = from statusChange in repo.DbContext.Set<NodeStatusChangeRecordModel>()
                        join nodeProfile in repo.DbContext.Set<NodeProfileModel>() on statusChange.NodeId equals nodeProfile.NodeInfoId
                        where (statusChange.CreationDateTime.Date >= parameters.BeginDateTime && statusChange.CreationDateTime <= parameters.EndDateTime)
                        && (parameters.NodeIdList.Count <= 0 || parameters.NodeIdList.Contains(statusChange.NodeId)) &&
                        (parameters.NodeStatus == NodeStatus.All ? true : statusChange.Status == parameters.NodeStatus)
                        group statusChange by new { statusChange.NodeId, statusChange.Status, statusChange.CreationDateTime.Date, statusChange.Name, nodeProfile.ClientVersion }
                        into g
                        where g.Count() > 0
                        orderby g.Key.Date descending, g.Count() descending
                        select new { g.Key.NodeId, g.Key.Date, g.Key.Name, g.Key.Status, g.Key.ClientVersion, Count = g.Count() };

            var count = await query.CountAsync(cancellationToken);

            var result = await query.Skip((parameters.PageIndex - 1) * parameters.PageSize)
                                    .Take(parameters.PageSize)
                                    .ToListAsync(cancellationToken);
            var list = result.Select(static x => new NodeStatusChangeStatisticItem()
            {
                NodeInfoId = x.NodeId,
                Status = x.Status,
                Count = x.Count,
                ClientVersion = x.ClientVersion,
                DateTime = x.Date,
                Name = x.Name
            }).ToList();
            return new ListQueryResult<NodeStatusChangeStatisticItem>(count, parameters.PageIndex, parameters.PageSize, list);
        }
    }
}
