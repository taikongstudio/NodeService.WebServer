using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/StatusChangeRecords/List")]
    public async Task<PaginationResponse<NodeStatusChangeRecordModel>> QueryNodeStatusChangeRecordListAsync(
        [FromQuery] QueryNodeStatusChangeRecordParameters queryParameters, CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeStatusChangeRecordModel>();
        try
        {
            await using var repo = await _recordRepoFactory.CreateRepositoryAsync(cancellationToken);
            var queryResult = await repo.PaginationQueryAsync(new NodeStatusChangeRecordSpecification(
                    queryParameters.Keywords,
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime,
                    DataFilterCollection<string>.Includes(queryParameters.NodeIdList),
                    queryParameters.SortDescriptions
                ),
                new PaginationInfo(
                    queryParameters.PageIndex,
                    queryParameters.PageSize)
                , cancellationToken
            );
            apiResponse.SetResult(queryResult);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}