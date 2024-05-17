using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/{id}/jobs/List")]
    public async Task<ApiResponse<IEnumerable<JobScheduleConfigModel>>> GetNodeTaskListAsync(string id)
    {
        var apiResponse = new ApiResponse<IEnumerable<JobScheduleConfigModel>>();
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
    public async Task<PaginationResponse<JobExecutionInstanceModel>> GetNodeTaskInstancesAsync(string nodeId,
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<JobExecutionInstanceModel>();
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
                var queryResult = await repo.ListAsync(new TaskExecutionInstanceSpecification(
                    queryParameters.Keywords,
                    queryParameters.Status,
                    queryParameters.NodeIdList,
                    queryParameters.TaskDefinitionIdList,
                    queryParameters.TaskExecutionInstanceIdList,
                    queryParameters.SortDescriptions));
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