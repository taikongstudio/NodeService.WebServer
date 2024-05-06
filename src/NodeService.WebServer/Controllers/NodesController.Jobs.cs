using Microsoft.IdentityModel.Tokens;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/nodes/{id}/jobs/list")]
    public async Task<ApiResponse<IEnumerable<JobScheduleConfigModel>>> GetNodeTaskListAsync(string id)
    {
        var apiResponse = new ApiResponse<IEnumerable<JobScheduleConfigModel>>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
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


    [HttpGet("/api/nodes/{id}/jobs/instances/list")]
    public async Task<ApiResponse<IEnumerable<JobExecutionInstanceModel>>> GetNodeJobInstancesAsync(string id,
       [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters)
    {
        var apiResponse = new ApiResponse<IEnumerable<JobExecutionInstanceModel>>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = $"invalid node id:{id}";
            }
            else
            {
                var queryable = dbContext.JobExecutionInstancesDbSet.AsQueryable()
                    .Where(x => x.NodeInfoId == nodeInfo.Id);
                if (queryParameters.JobScheduleConfigIdList != null && queryParameters.JobScheduleConfigIdList.Any())
                    queryable = queryable.Where(x => queryParameters.JobScheduleConfigIdList.Contains(x.JobScheduleConfigId));
                if (!string.IsNullOrEmpty(queryParameters.Keywords))
                    queryable = queryable.Where(x => x.Name.Contains(queryParameters.Keywords));
                apiResponse = await queryable.QueryPageItemsAsync(queryParameters);
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