


using Microsoft.AspNetCore.Mvc;
using NodeService.WebServer.Controllers;
using NodeService.WebServer.Data;
using NodeService.WebServer.Extensions;
using NodeService.Infrastructure.Interfaces;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Services.JobSchedule;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class JobsController : Controller
    {
        private readonly ILogger<NodesController> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly INodeSessionService _nodeSessionService;
        private readonly IVirtualFileSystem _virtualFileSystem;
        private readonly IMemoryCache _memoryCache;
        private readonly RocksDatabase _database;
        private readonly JobExecutionInstanceInitializer _jobExecutionInstanceInitializer;

        public JobsController(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            INodeSessionService  nodeSessionService,
            ILogger<NodesController> logger,
            IVirtualFileSystem virtualFileSystem,
            IMemoryCache memoryCache,
            JobExecutionInstanceInitializer jobExecutionInstanceInitializer,
            RocksDatabase database)
        {
            this._logger = logger;
            this._dbContextFactory = dbContextFactory;
            this._nodeSessionService = nodeSessionService;
            this._virtualFileSystem = virtualFileSystem;
            this._memoryCache = memoryCache;
            _database = database;
            _jobExecutionInstanceInitializer = jobExecutionInstanceInitializer;
        }

        [HttpGet("/api/jobs/instances/list")]
        public async Task<ApiResponse<IEnumerable<JobExecutionInstanceModel>>> QueryJobExecutionInstanceListAsync(
            QueryJobExecutionInstanceParameters parameters
            )
        {
            ApiResponse<IEnumerable<JobExecutionInstanceModel>> apiResponse = new ApiResponse<IEnumerable<JobExecutionInstanceModel>>();
            try
            {
                if (parameters.BeginDateTime == null)
                {
                    parameters.BeginDateTime = DateTime.Today.Date;
                }
                if (parameters.EndDateTime == null)
                {
                    parameters.EndDateTime = DateTime.Today.Date.AddDays(1).AddSeconds(-1);
                }
                using var dbContext = _dbContextFactory.CreateDbContext();
                var beginTime = parameters.BeginDateTime.Value;
                var endTime = parameters.EndDateTime.Value;
                IQueryable<JobExecutionInstanceModel> queryable = dbContext.JobExecutionInstancesDbSet;
                if (parameters.NodeId != null)
                {
                    queryable = queryable.Where(x => x.NodeInfoId == parameters.NodeId);
                }
                if (parameters.Status != null)
                {
                    var value = parameters.Status.Value;
                    queryable = queryable.Where(x => x.Status == value);
                }
                if (parameters.JobScheduleConfigId != null)
                {
                    queryable = queryable.Where(x => x.JobScheduleConfigId == parameters.JobScheduleConfigId);
                }
                var result = await queryable
                    .OrderByDescending(x => x.FireTime)
                    .Where(x => x.FireTime >= beginTime && x.FireTime < endTime)
                    .Select(x => new
                    {
                        x.Id,
                        x.CancelTimes,
                        x.Status,
                        x.JobScheduleConfigId,
                        x.ReinvokeTimes,
                        x.ParentId,
                        x.NodeInfoId,
                        x.NextFireTimeUtc,
                        x.PreviousFireTimeUtc,
                        x.ScheduledFireTimeUtc,
                        x.EntityVersion,
                        x.TriggerSource,
                        x.Name,
                        x.Message,
                        x.LogPath,
                        x.FireTime,
                        x.FireType,
                        x.FireInstanceId,
                        x.ExecutionBeginTime,
                        x.ExecutionEndTime,
                        x.Description,
                    })
                    .IgnoreAutoIncludes()
                    .AsSplitQuery()
                    .ToListAsync();
                apiResponse.Result = result.Select(x => new JobExecutionInstanceModel()
                {
                    Description = x.Description,
                    CancelTimes = x.CancelTimes,
                    ExecutionBeginTime = x.ExecutionBeginTime,
                    ExecutionEndTime = x.ExecutionEndTime,
                    FireInstanceId = x.FireInstanceId,
                    FireTime = x.FireTime,
                    FireType = x.FireType,
                    Id = x.Id,
                    JobScheduleConfigId = x.JobScheduleConfigId,
                    LogPath = x.LogPath,
                    Message = x.Message,
                    Name = x.Name,
                    NextFireTimeUtc = x.NextFireTimeUtc,
                    NodeInfoId = x.NodeInfoId,
                    ParentId = x.ParentId,
                    PreviousFireTimeUtc = x.PreviousFireTimeUtc,
                    ReinvokeTimes = x.ReinvokeTimes,
                    ScheduledFireTimeUtc = x.ScheduledFireTimeUtc,
                    Status = x.Status,
                    TriggerSource = x.TriggerSource,
                    EntityVersion = x.EntityVersion,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/jobs/instances/{id}/reinvoke")]
        public async Task<ApiResponse<JobExecutionInstanceModel>> ReinvokeAsync(string id, [FromBody] object obj)
        {
            ApiResponse<JobExecutionInstanceModel> apiResponse = new ApiResponse<JobExecutionInstanceModel>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var jobExecutionInstance = await dbContext.JobExecutionInstancesDbSet.FindAsync(id);
                if (jobExecutionInstance == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "invalid job execution instance id";
                }
                else
                {
                    jobExecutionInstance.ReinvokeTimes++;
                    var nodeId = new NodeId(id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var rsp = await _nodeSessionService.SendJobExecutionEventAsync(nodeSessionId, jobExecutionInstance.ToReinvokeEvent());
                    }
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/jobs/instances/{id}/cancel")]
        public async Task<ApiResponse<JobExecutionInstanceModel>> CancelAsync(string id, [FromBody] object from)
        {
            ApiResponse<JobExecutionInstanceModel> apiResponse = new ApiResponse<JobExecutionInstanceModel>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var jobExecutionInstance = await dbContext.JobExecutionInstancesDbSet.FindAsync(id);
                if (jobExecutionInstance == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "invalid job execution instance id";
                }
                else
                {
                    jobExecutionInstance.CancelTimes++;
                    await dbContext.SaveChangesAsync();
                    await _jobExecutionInstanceInitializer.CancelAsync(jobExecutionInstance);
                    var rsp = await _nodeSessionService.SendJobExecutionEventAsync(
                        new NodeSessionId(jobExecutionInstance.NodeInfoId),
                        jobExecutionInstance.ToCancelEvent());
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



        [HttpGet("/api/jobs/instances/{id}/logs")]
        public async Task<PaginationResponse<LogMessageEntry>> QueryLogsAsync(string id, QueryParametersModel queryParameters)
        {
            PaginationResponse<LogMessageEntry> apiResponse = new PaginationResponse<LogMessageEntry>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var jobExecutionInstance = await dbContext.JobExecutionInstancesDbSet.FindAsync(id);
                if (jobExecutionInstance == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "invalid job execution instance id";
                }
                else
                {
                    apiResponse.TotalCount = _database.GetEntriesCount(id);
                    apiResponse.Result = _database.ReadEntries<LogMessageEntry>(
                        id,
                        queryParameters.PageIndex,
                        queryParameters.PageSize)
                        .OrderBy(static x => x.Index);
                    apiResponse.PageIndex = queryParameters.PageIndex;
                    apiResponse.PageSize = queryParameters.PageSize;
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
