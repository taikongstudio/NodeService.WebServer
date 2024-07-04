using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Data.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NodeService.Infrastructure.Models;
using Ardalis.Specification;
using NodeService.WebServer.Data;

namespace NodeService.WebServer.Services.QueryOptimize
{
    public abstract class QueryHandler<TQueryParameters, TResult> where TQueryParameters : PaginationQueryParameters where TResult : JsonBasedDataModel
    {
        private readonly ILogger _logger;
        private readonly IMemoryCache _memoryCache;

        protected QueryHandler(ILogger logger, IMemoryCache memoryCache)
        {
            _logger = logger;
            _memoryCache = memoryCache;
        }

        protected abstract ISpecification<TResult> GetSpecification(QueryParameters queryParameters);

        public virtual async ValueTask<ListQueryResult<TResult>> ExecuteQuery(IServiceProvider serviceProvider, TQueryParameters queryParameters)
        {
            var repoFactory = serviceProvider.GetService<ApplicationRepositoryFactory<TResult>>();
            _logger.LogInformation($"{typeof(TResult).FullName}:{queryParameters}");
            using var repo = repoFactory.CreateRepository();
            ListQueryResult<TResult> listQueryResult = default;
            if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
            {
                listQueryResult = await repo.PaginationQueryAsync(
                                                            GetSpecification(queryParameters),
                                                            queryParameters.PageSize,
                                                            queryParameters.PageIndex);
            }
            else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
            {
                var key = $"{typeof(TResult).FullName}:{queryParameters}";

                if (!_memoryCache.TryGetValue(key, out listQueryResult) || !listQueryResult.HasValue)
                {
                    listQueryResult = await repo.PaginationQueryAsync(
                                                                GetSpecification(queryParameters),
                                                                queryParameters.PageSize,
                                                                queryParameters.PageIndex);

                    if (listQueryResult.HasValue)
                        _memoryCache.Set(key, listQueryResult, TimeSpan.FromMinutes(1));
                }
            }
            return listQueryResult;
        }

    }

    public class DefaultConfigurationQueryHandler<TResult> : QueryHandler<PaginationQueryParameters, TResult>
        where TResult : JsonBasedDataModel
    {
        ILogger<DefaultConfigurationQueryHandler<TResult>> _logger;
        readonly IMemoryCache _memoryCache;

        public DefaultConfigurationQueryHandler(
            ILogger<DefaultConfigurationQueryHandler<TResult>> logger,
            IMemoryCache memoryCache) : base(logger, memoryCache)
        {
            _logger = logger;
            _memoryCache = memoryCache;
        }

        protected override ISpecification<TResult> GetSpecification(QueryParameters queryParameters)
        {
            if (queryParameters is QueryTaskDefinitionParameters queryTaskDefinitionParameters)
            {
                return new CommonConfigurationSpecification<TResult>(DataFilterCollection<string>.Includes(queryTaskDefinitionParameters.IdList));
            }
            return new CommonConfigurationSpecification<TResult>(queryParameters.Keywords, queryParameters.SortDescriptions);
        }
    }

}
