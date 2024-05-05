using NodeService.Infrastructure.Models;
using System.Linq;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NotificationController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<NotificationController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    [HttpGet("/api/notification/record/list")]
    public async Task<PaginationResponse<NotificationRecordModel>> QueryNotificationRecordListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<NotificationRecordModel>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();

            IQueryable<NotificationRecordModel> queryable = dbContext.NotificationRecordsDbSet;
            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;
            var startIndex = pageIndex * pageSize;

            var totalCount = await queryable.CountAsync();

            var items = await queryable.Skip(startIndex)
                                       .Take(pageSize)
                                       .ToArrayAsync();

            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
            if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;
            apiResponse.PageIndex = queryParameters.PageIndex;
            apiResponse.PageSize = queryParameters.PageSize;
            apiResponse.TotalCount = totalCount;
            apiResponse.Result = items;
        }
        catch (Exception ex)
        {
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}