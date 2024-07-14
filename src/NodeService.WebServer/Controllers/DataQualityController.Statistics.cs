using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class DataQualityController
{
    [HttpGet("/api/DataQuality/Statistics/List")]
    public async Task<PaginationResponse<DataQualityNodeStatisticsRecordModel>>
        QueryDataQualityNodeStatisticsReportListAsync(
            [FromQuery] QueryDataQualityNodeStatisticsReportParameters queryParameters,
            CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<DataQualityNodeStatisticsRecordModel>();
        try
        {
            await using var recordRepo = await _nodeStatisticsRecordRepoFactory.CreateRepositoryAsync();
            var queryResult = await recordRepo.PaginationQueryAsync(
                new DataQualityStatisticsSelectSpecification<DataQualityNodeStatisticsRecordModel>(
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime,
                    DataFilterCollection<string>.Includes(queryParameters.NodeIdList)),
                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);
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

    [HttpGet("/api/DataQuality/Calendar/List")]
    public async Task<PaginationResponse<DataQualityCalendarEntry>> QueryDataQualityCalendarListAsync(
        [FromQuery] QueryDataQualityStatisticsCalendarParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<DataQualityCalendarEntry>();
        try
        {
            await using var recordRepo = await _nodeStatisticsRecordRepoFactory.CreateRepositoryAsync();
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();

            var queryResult = await recordRepo.PaginationQueryAsync(
                new DataQualityStatisticsSelectSpecification<DataQualityNodeStatisticsRecordModel>(
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime),
                0, 0,
                cancellationToken);

            List<DataQualityCalendarEntry> calendarEntries = new();

            foreach (var dateTimeGroups in queryResult.Items.GroupBy(static x => x.DateTime))
            {
                var dateTime = dateTimeGroups.Key;
                var calendarEntry = new DataQualityCalendarEntry()
                {
                    DateTime = dateTime
                };
                calendarEntries.Add(calendarEntry);
                foreach (var record in dateTimeGroups)
                foreach (var entry in record.Entries)
                {
                    if (entry.Name != queryParameters.Name) continue;
                    entry.NodeName = record.Name;
                    calendarEntry.Entries.Add(entry);
                    if (entry.Value == null)
                    {
                        calendarEntry.FaultCount++;
                    }
                    else
                    {
                        var value = entry.Value.GetValueOrDefault();
                        if (value == 1)
                            calendarEntry.CompletedCount++;
                        else if (value < 1 && value > 0) calendarEntry.InprogressCount++;
                    }
                }
            }

            apiResponse.SetResult(new ListQueryResult<DataQualityCalendarEntry>(
                calendarEntries.Count,
                queryParameters.PageIndex,
                queryParameters.PageSize,
                calendarEntries));
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

    //    [HttpGet("/api/DataQuality/Statistics/Summary")]
    //    public async Task<PaginationResponse<DataQualityNodeStatisticsRecordModel>> QueryDataQualityNodeStatisticsReportListAsync(
    //[FromQuery] QueryParameters queryParameters,
    //CancellationToken cancellationToken = default)
    //    {
    //        var apiResponse = new PaginationResponse<DataQualityNodeStatisticsRecordModel>();
    //        try
    //        {
    //            using var recordRepo = _nodeStatisticsRecordRepoFactory.CreateRepository();
    //            var queryResult = await recordRepo.PaginationQueryAsync(
    //                new DataQualityStatisticsSpecification(queryParameters.BeginDateTime, queryParameters.EndDateTime),
    //                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
    //                cancellationToken);
    //            apiResponse.SetResult(queryResult);
    //        }
    //        catch (Exception ex)
    //        {
    //            _exceptionCounter.AddOrUpdate(ex);
    //            _logger.LogError(ex.ToString());
    //            apiResponse.ErrorCode = ex.HResult;
    //            apiResponse.Message = ex.ToString();
    //        }

    //        return apiResponse;
    //    }
}