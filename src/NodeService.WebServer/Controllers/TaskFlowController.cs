﻿using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers
{
    public class TaskFlowController : Controller
    {
        readonly ILogger<TaskFlowController> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;

        public TaskFlowController(
            ILogger<TaskFlowController> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        }

        [HttpGet("/api/TaskFlows/Instances/List")]
        public async Task<PaginationResponse<TaskFlowExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
    [FromQuery] PaginationQueryParameters queryParameters
)
        {
            var apiResponse = new PaginationResponse<TaskFlowExecutionInstanceModel>();
            try
            {
                using var repo = _taskFlowExecutionInstanceRepoFactory.CreateRepository();
                var queryResult = await repo.PaginationQueryAsync(new TaskFlowExecutionInstanceSpecification(
                        queryParameters.Keywords,
                        queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex
                );
                apiResponse.SetResult(queryResult);
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


        [HttpGet("/api/TaskFlows/Instances/{taskFlowExecutionInstanceId}")]
        public async Task<ApiResponse<TaskFlowExecutionInstanceModel>>
            QueryTaskExecutionInstanceAsync(string taskFlowExecutionInstanceId)
        {
            var apiResponse = new ApiResponse<TaskFlowExecutionInstanceModel>();
            try
            {
                using var repo = _taskFlowExecutionInstanceRepoFactory.CreateRepository();
                var queryResult = await repo.GetByIdAsync(taskFlowExecutionInstanceId);
                apiResponse.SetResult(queryResult);
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
}
