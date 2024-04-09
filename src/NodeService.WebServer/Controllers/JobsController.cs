﻿


using Microsoft.AspNetCore.Mvc;
using NodeService.WebServer.Controllers;
using NodeService.WebServer.Data;
using NodeService.WebServer.Extensions;
using NodeService.Infrastructure.Interfaces;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Services.Tasks;

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
                    parameters.BeginDateTime = DateTime.Today.ToUniversalTime().Date;
                }
                if (parameters.EndDateTime == null)
                {
                    parameters.EndDateTime = DateTime.Today.ToUniversalTime().Date.AddDays(1).AddSeconds(-1);
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
                    .OrderByDescending(x => x.FireTimeUtc)
                    .Where(x => x.FireTimeUtc >= beginTime && x.FireTimeUtc < endTime)
                    .IgnoreAutoIncludes()
                    .AsSplitQuery()
                    .ToListAsync();
                apiResponse.Result = result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpGet("/api/jobs/instances/{id}/reinvoke")]
        public async Task<ApiResponse<JobExecutionInstanceModel>> ReinvokeAsync(string id)
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
                    await _jobExecutionInstanceInitializer.TryCancelAsync(jobExecutionInstance.Id);
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



        [HttpGet("/api/jobs/instances/{id}/log")]
        public async Task<PaginationResponse<LogMessageEntry>> QueryJobLogAsync(string id, QueryParametersModel queryParameters)
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
