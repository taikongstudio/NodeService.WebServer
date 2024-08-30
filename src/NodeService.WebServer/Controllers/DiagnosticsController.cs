using Microsoft.AspNetCore.Mvc;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Services.Counters;
using System.Reflection.Metadata.Ecma335;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class DiagnosticsController : Controller
    {
        readonly ExceptionCounter _exceptionCounter;
        readonly WebServerCounter _webServerCounter;

        public DiagnosticsController(
            ExceptionCounter exceptionCounter,
            WebServerCounter webServerCounter)
        {
            _exceptionCounter = exceptionCounter;
            _webServerCounter = webServerCounter;
        }

        [HttpGet("/api/Diagnostics/ExceptionStatistics")]
        public Task<PaginationResponse<ExceptionEntry>> GetExceptionStatisticsAsync()
        {
            var exceptionStatistics = _exceptionCounter.GetStatistics().ToArray();
            var rsp = new PaginationResponse<ExceptionEntry>();
            rsp.SetResult(new ListQueryResult<ExceptionEntry>(
                exceptionStatistics.Length,
                1,
                exceptionStatistics.Length,
                exceptionStatistics));
            return Task.FromResult(rsp);
        }

        [HttpGet("/api/Diagnostics/Counters")]
        public Task<ApiResponse<WebServerCounterSnapshot>> GetCountersAsync()
        {
            var rsp = new ApiResponse<WebServerCounterSnapshot>();
            rsp.SetResult(_webServerCounter.Snapshot);
            return Task.FromResult(rsp);
        }
    }
}
