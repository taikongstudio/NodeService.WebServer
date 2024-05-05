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
        [FromQuery] QueryParameters queryParameters)
    {
        var rsp = new PaginationResponse<NotificationRecordModel>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            rsp.Result = await dbContext.NotificationRecordsDbSet.OrderByDescending(x => x.CreationDateTime)
                .ToArrayAsync();
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}