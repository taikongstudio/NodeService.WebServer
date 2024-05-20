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
                    new DataQualityStatisticsSpecification(queryParameters.BeginDateTime, queryParameters.EndDateTime),
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

    }
}
