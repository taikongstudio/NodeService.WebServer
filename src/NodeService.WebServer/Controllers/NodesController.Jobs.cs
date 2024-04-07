

using Microsoft.EntityFrameworkCore;

namespace NodeService.WebServer.Controllers
{
    public partial class NodesController
    {

        [HttpGet("/api/nodes/{id}/jobs/list")]
        public async Task<ApiResponse<IEnumerable<JobScheduleConfigModel>>> GetNodeTaskListAsync(string id)
        {
            ApiResponse<IEnumerable<JobScheduleConfigModel>> apiResponse = new ApiResponse<IEnumerable<JobScheduleConfigModel>>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
                if (nodeInfo == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = $"invalid node id:{id}";
                }
                else
                {
                    apiResponse.Result = [];
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }


        [HttpGet("/api/nodes/{id}/jobs/instances/list")]
        public async Task<ApiResponse<IEnumerable<JobExecutionInstanceModel>>> GetNodeJobInstancesAsync(string id, [FromQuery] string jobScheduleConfigId)
        {
            ApiResponse<IEnumerable<JobExecutionInstanceModel>> apiResponse = new ApiResponse<IEnumerable<JobExecutionInstanceModel>>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
                if (nodeInfo == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = $"invalid node id:{id}";
                }
                else
                {
                    var queryable = dbContext.JobExecutionInstancesDbSet.AsQueryable().Where(x => x.NodeInfoId == nodeInfo.Id);
                    if (!string.IsNullOrEmpty(jobScheduleConfigId))
                    {
                        queryable = queryable.Where(x => x.JobScheduleConfigId == jobScheduleConfigId);
                    }
                    apiResponse.Result = await queryable.ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

    }
}
