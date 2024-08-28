using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NotificationController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<NotificationController> _logger;
    private readonly ApplicationRepositoryFactory<NotificationRecordModel> _notificationRecordsRepositoryFactory;

    public NotificationController(
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NotificationRecordModel> notificationRecordsRepositoryFactory,
        ILogger<NotificationController> logger)
    {
        _notificationRecordsRepositoryFactory = notificationRecordsRepositoryFactory;
        _logger = logger;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/Notification/Records/List")]
    public async Task<PaginationResponse<NotificationRecordModel>> QueryNotificationRecordListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<NotificationRecordModel>();
        try
        {
            await using var repo = await _notificationRecordsRepositoryFactory.CreateRepositoryAsync();
            var queryResult = await repo.PaginationQueryAsync(
                new NotificationRecordSpecification(
                    queryParameters.Keywords,
                    queryParameters.SortDescriptions),
                queryParameters.PageSize,
                queryParameters.PageIndex);
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

    [HttpGet("/api/Notification/Records/Details/{id}")]
    public async Task<ApiResponse<NotificationRecordModel>> QueryNotificationRecordAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<NotificationRecordModel>();
        try
        {
            await using var repo = await _notificationRecordsRepositoryFactory.CreateRepositoryAsync();
            var queryResult = await repo.GetByIdAsync(id, cancellationToken);
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