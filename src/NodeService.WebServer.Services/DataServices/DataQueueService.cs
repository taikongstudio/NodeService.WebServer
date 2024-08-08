using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.DataServices
{

    public partial class DataQueueService<TEntity> : BackgroundService
        where TEntity : RecordBase
    {

        readonly BatchQueue<DataQueueService<TEntity>.AsyncOperation> _batchQueue;
        readonly ILogger<DataQueueService<TEntity>> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ApplicationRepositoryFactory<TEntity> _repoFactory;

        public DataQueueService(
            ILogger<DataQueueService<TEntity>> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<TEntity> repoFactory,
            BatchQueue<DataQueueService<TEntity>.AsyncOperation> batchQueue)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _repoFactory = repoFactory;
            _batchQueue = batchQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var ops in _batchQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    if (ops == null || ops.Length == 0)
                    {
                        continue;
                    }
                    await using var nodeRepo = await _repoFactory.CreateRepositoryAsync();
                    foreach (var op in ops)
                    {
                        try
                        {
                            if (op == null)
                            {
                                continue;
                            }
                            switch (op.Kind)
                            {
                                case AsyncOperationKind.None:
                                    break;
                                case AsyncOperationKind.AddOrUpdate:
                                    {
                                        var parameters = op.Argument;
                                        foreach (var nodeInfo in parameters.Parameters.AsT0)
                                        {
                                            var nodeInfoFromDb = await nodeRepo.GetByIdAsync(nodeInfo.Id, cancellationToken);
                                            if (nodeInfoFromDb == null)
                                            {
                                                await nodeRepo.AddAsync(nodeInfo, cancellationToken);
                                            }
                                            else
                                            {
                                                nodeInfoFromDb.With(nodeInfo);
                                                await nodeRepo.UpdateAsync(nodeInfoFromDb, cancellationToken);
                                            }
                                        }

                                        op.TrySetResult(new DataQueueServiceResult<TEntity>(parameters.Parameters.AsT0));
                                    }

                                    break;
                                case AsyncOperationKind.Delete:
                                    {
                                        var parameters = op.Argument;
                                        await nodeRepo.DeleteRangeAsync(parameters.Parameters.AsT0, cancellationToken);
                                        op.TrySetResult(new DataQueueServiceResult<TEntity>(parameters.Parameters.AsT0));
                                    }
                                    break;
                                case AsyncOperationKind.Query:
                                    {
                                        var parameters = op.Argument;
                                        var idFilters = DataFilterCollection<string>.Includes(parameters.Parameters.AsT1);
                                        var nodeInfoList = await nodeRepo.ListAsync(new ListSpecification<TEntity>(idFilters), cancellationToken);
                                        op.TrySetResult(new DataQueueServiceResult<TEntity>([.. nodeInfoList]));
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            op.TrySetException(ex);
                            _exceptionCounter.AddOrUpdate(ex);
                            _logger.LogError(ex.ToString());
                        }


                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            }
        }
    }
}
