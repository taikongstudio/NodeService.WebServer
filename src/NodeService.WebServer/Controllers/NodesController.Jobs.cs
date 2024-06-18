using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/~/{id}/jobs/List")]
    public async Task<ApiResponse<IEnumerable<TaskDefinitionModel>>> GetNodeTaskListAsync(string id)
    {
        var apiResponse = new ApiResponse<IEnumerable<TaskDefinitionModel>>();
        try
        {
            using var repository = _nodeInfoRepoFactory.CreateRepository();
            var nodeInfo = await repository.GetByIdAsync(id);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = $"invalid node id:{id}";
            }
            else
            {
                apiResponse.SetResult([]);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }


    [HttpGet("/api/Nodes/{nodeId}/jobs/instances/List")]
    public async Task<PaginationResponse<TaskExecutionInstanceModel>> GetNodeTaskInstancesAsync(
        string nodeId,
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<TaskExecutionInstanceModel>();
        try
        {
            using var repository = _nodeInfoRepoFactory.CreateRepository();
            var nodeInfo = await repository.GetByIdAsync(nodeId);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = $"invalid node id:{nodeId}";
            }
            else
            {
                queryParameters.NodeIdList = [nodeId];
                using var repo = _taskExecutionInstanceRepoFactory.CreateRepository();
                var queryResult = await repo.PaginationQueryAsync(new TaskExecutionInstanceSpecification(
                        queryParameters.Keywords,
                        queryParameters.Status,
                        queryParameters.NodeIdList,
                        queryParameters.TaskDefinitionIdList,
                        queryParameters.TaskExecutionInstanceIdList,
                        queryParameters.SortDescriptions),
                    new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                    cancellationToken);
                apiResponse.SetResult(queryResult);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}