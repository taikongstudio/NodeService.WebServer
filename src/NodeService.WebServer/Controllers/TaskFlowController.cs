using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers
{
    public class TaskFlowController : Controller
    {
        readonly ILogger<TaskFlowController> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
        readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;

        public TaskFlowController(
            ILogger<TaskFlowController> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
            BatchQueue<TaskActivateServiceParameters> taskActivateServiceParametersBatchQueue
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
            _taskActivateServiceParametersBatchQueue = taskActivateServiceParametersBatchQueue;
        }

        [HttpGet("/api/TaskFlows/Instances/List")]
        public async Task<PaginationResponse<TaskFlowExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
    [FromQuery] QueryTaskFlowExecutionInstanceListParameters queryParameters
)
        {
            var apiResponse = new PaginationResponse<TaskFlowExecutionInstanceModel>();
            try
            {
                await using var repo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync();
                var queryResult = await repo.PaginationQueryAsync(new TaskFlowExecutionInstanceSpecification(
                        queryParameters.Keywords,
                        queryParameters.Status,
                        queryParameters.BeginDateTime,
                        queryParameters.EndDateTime,
                       DataFilterCollection<string>.Includes(queryParameters.TaskFlowTemplateIdList),
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
            QueryTaskExecutionInstanceAsync(
            string taskFlowExecutionInstanceId,
            CancellationToken cancellationToken = default)
        {
            var apiResponse = new ApiResponse<TaskFlowExecutionInstanceModel>();
            try
            {
                await using var repo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
                var queryResult = await repo.GetByIdAsync(taskFlowExecutionInstanceId, cancellationToken);
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

        [HttpPost("/api/TaskFlows/Instances/{taskFlowExecutionInstanceId}/SwitchStage")]
        public async Task<ApiResponse<TaskFlowExecutionInstanceModel>> SwitchStageAsync(
            string taskFlowExecutionInstanceId,
            [FromBody] TaskFlowSwitchStageParameters parameters,
            CancellationToken cancellationToken = default)
        {
            var apiResponse = new ApiResponse<TaskFlowExecutionInstanceModel>();
            try
            {
                var switchTaskFlowStageParameters = new SwitchStageParameters(
                    taskFlowExecutionInstanceId,
                    parameters.StageIndex);
                await _taskActivateServiceParametersBatchQueue.SendAsync(
                    new TaskActivateServiceParameters(switchTaskFlowStageParameters),
                    cancellationToken);
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
