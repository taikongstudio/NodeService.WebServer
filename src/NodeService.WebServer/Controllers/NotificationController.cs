﻿using NodeService.Infrastructure.Models;
using NodeService.WebServer.Models;
using System.Linq;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NotificationController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<NotificationController> _logger;
    private readonly ExceptionCounter _exceptionCounter;

    public NotificationController(
        ExceptionCounter exceptionCounter,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<NotificationController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/notification/record/list")]
    public async Task<PaginationResponse<NotificationRecordModel>> QueryNotificationRecordListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<NotificationRecordModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            IQueryable<NotificationRecordModel> queryable = dbContext.NotificationRecordsDbSet;

            queryable = queryable.OrderBy(queryParameters.SortDescriptions);

            apiResponse = await queryable.QueryPageItemsAsync(queryParameters);
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