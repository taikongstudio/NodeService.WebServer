using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers
{
    public partial class DataQualityController
    {

        [HttpGet("/api/DataQuality/Statistics/List")]
        public async Task<PaginationResponse<DataQualityNodeStatisticsRecordModel>> QueryDataQualityNodeStatisticsReportListAsync(
            [FromQuery] QueryDataQualityNodeStatisticsReportParameters queryParameters,
            CancellationToken cancellationToken = default)
        {
            var apiResponse = new PaginationResponse<DataQualityNodeStatisticsRecordModel>();
            try
            {
                using var recordRepo = _nodeStatisticsRecordRepoFactory.CreateRepository();
                var queryResult = await recordRepo.PaginationQueryAsync(
                    new DataQualityStatisticsSpecification(
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
                using var recordRepo = _nodeStatisticsRecordRepoFactory.CreateRepository();
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                var queryResult = await recordRepo.PaginationQueryAsync(
                    new DataQualityStatisticsSpecification(
                        queryParameters.BeginDateTime,
                        queryParameters.EndDateTime),
                    new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                    cancellationToken);

                List<DataQualityCalendarEntry> calendarEntries = new List<DataQualityCalendarEntry>();

                foreach (var group in queryResult.Items.GroupBy(static x => x.NodeId))
                {
                    if (group.Key == null)
                    {
                        continue;
                    }
                    var nodeInfo = await nodeInfoRepo.GetByIdAsync(group.Key, cancellationToken);
                    if (nodeInfo == null)
                    {
                        continue;
                    }
                    foreach (var item in group)
                    {
                        var calendarEntry = new DataQualityCalendarEntry()
                        {
                            DateTime = item.DateTime,
                            Entries = item.Entries.Where(x => x.Name == queryParameters.Name).Select(x =>
                            {
                                x.NodeName = nodeInfo.Name;
                                return x;
                            }),
                        };
                        calendarEntries.Add(calendarEntry);
                        foreach (var entry in calendarEntry.Entries)
                        {
                            if (entry.Value == null)
                            {
                                calendarEntry.FaultCount++;
                            }
                            else
                            {
                                var value = entry.Value.GetValueOrDefault();
                                if (value == 1)
                                {
                                    calendarEntry.CompletedCount++;
                                }
                                else if (value < 1 && value > 0)
                                {
                                    calendarEntry.InprogressCount++;
                                }
                            }
                        }

                    }
                }

                apiResponse.SetResult(calendarEntries);
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
}
