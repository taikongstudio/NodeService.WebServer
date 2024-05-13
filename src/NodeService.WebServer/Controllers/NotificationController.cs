﻿using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using System.Linq;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NotificationController : Controller
{
    readonly ApplicationRepositoryFactory<NotificationRecordModel> _notificationRecordsRepositoryFactory;
    readonly ILogger<NotificationController> _logger;
    readonly ExceptionCounter _exceptionCounter;

    public NotificationController(
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NotificationRecordModel> notificationRecordsRepositoryFactory,
        ILogger<NotificationController> logger)
    {
        _notificationRecordsRepositoryFactory = notificationRecordsRepositoryFactory;
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
            using var repo = _notificationRecordsRepositoryFactory.CreateRepository();
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
}