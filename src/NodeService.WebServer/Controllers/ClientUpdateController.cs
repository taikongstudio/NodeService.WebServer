using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using System.Drawing.Printing;
using static NuGet.Protocol.Core.Types.Repository;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ClientUpdateController : Controller
{
    readonly ILogger<ClientUpdateController> _logger;
    private readonly ApplicationRepositoryFactory<ClientUpdateConfigModel> _clientUpdateRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ApplicationRepositoryFactory<ClientUpdateCounterModel> _clientUpdateCounterRepoFactory;
    readonly IMemoryCache _memoryCache;
    readonly ExceptionCounter _exceptionCounter;

    public ClientUpdateController(
        ILogger<ClientUpdateController> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<ClientUpdateConfigModel> clientUpdateRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<ClientUpdateCounterModel> clientUpdateCounterRepoFactory,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _clientUpdateRepoFactory = clientUpdateRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _clientUpdateCounterRepoFactory = clientUpdateCounterRepoFactory;
        _memoryCache = memoryCache;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/clientupdate/getupdate")]
    public async Task<ApiResponse<ClientUpdateConfigModel>> GetUpdateAsync([FromQuery] string? name)
    {
        var apiResponse = new ApiResponse<ClientUpdateConfigModel>();
        try
        {
            ClientUpdateConfigModel? clientUpdateConfig = null;
            if (string.IsNullOrEmpty(name)) return apiResponse;
            var key = $"ClientUpdateConfig:{name}";
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 1000)));
            if (!_memoryCache.TryGetValue<ClientUpdateConfigModel>(key, out var cacheValue) || cacheValue == null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1000)));
                if (!_memoryCache.TryGetValue(key, out cacheValue) || cacheValue == null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1000)));
                    await using var repo = _clientUpdateRepoFactory.CreateRepository();

                    cacheValue = await repo.FirstOrDefaultAsync(new ClientUpdateConfigSpecification(ClientUpdateStatus.Public, name));

                    if (cacheValue != null)
                    {
                        _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(1));
                    }
                }
            }

            if (cacheValue != null)
            {
                clientUpdateConfig = cacheValue;
            }


            if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null &&
                clientUpdateConfig.DnsFilters.Any())
            {
                await using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                var ipAddress = HttpContext.Connection.RemoteIpAddress.ToString();

                DataFilterTypes dataFilterType = DataFilterTypes.None;

                if (clientUpdateConfig.DnsFilterType.Equals("include", StringComparison.OrdinalIgnoreCase))
                {
                    dataFilterType = DataFilterTypes.Include;
                }

                else if (clientUpdateConfig.DnsFilterType.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                {
                    dataFilterType = DataFilterTypes.Exclude;
                }
                var nodeList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    DataFilterCollection<string>.Empty,
                   new DataFilterCollection<string>(
                       dataFilterType,
                       clientUpdateConfig.DnsFilters.Select(x => x.Value))));
                if (nodeList.Count != 0)
                {
                    if (dataFilterType == DataFilterTypes.Include)
                    {
                        apiResponse.SetResult(clientUpdateConfig);
                    }
                    else if (dataFilterType == DataFilterTypes.Exclude)
                    {
                        apiResponse.SetResult(null);
                    }

                }
            }
            else
            {
                apiResponse.SetResult(clientUpdateConfig);
            }
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

    [HttpPost("/api/clientupdate/addorupdate")]
    public async Task<ApiResponse> AddOrUpdateAsync([FromBody] ClientUpdateConfigModel model)
    {
        var apiResponse = new ApiResponse();
        try
        {
            using var repo = _clientUpdateRepoFactory.CreateRepository();
            var modelFromRepo = await repo.GetByIdAsync(model.Id);
            model.PackageConfig = null;
            if (modelFromRepo == null)
            {
                await repo.AddAsync(model);
            }
            else
            {
                await repo.UpdateAsync(model);
            }
            var key = $"ClientUpdateConfig:{model.Name}";
            _memoryCache.Remove(key);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpGet("/api/clientupdate/list")]
    public async Task<PaginationResponse<ClientUpdateConfigModel>> QueryClientUpdateListAsync(
        [FromQuery] PaginationQueryParameters queryParameters
    )
    {
        var apiResponse = new PaginationResponse<ClientUpdateConfigModel>();
        try
        {
            using var repo = _clientUpdateRepoFactory.CreateRepository();
            var queryResult = await repo.PaginationQueryAsync(
                new ClientUpdateConfigSpecification(
                    queryParameters.Keywords,
                    queryParameters.SortDescriptions),
                queryParameters.PageSize,
                queryParameters.PageIndex);
            apiResponse.SetResult(queryResult);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }



    [HttpPost("/api/clientupdate/remove")]
    public async Task<ApiResponse> RemoveAsync([FromBody] ClientUpdateConfigModel model)
    {
        var apiResponse = new ApiResponse();
        try
        {
            using var repo = _clientUpdateRepoFactory.CreateRepository();
            await repo.DeleteAsync(model);
            var key = $"ClientUpdateConfig:{model.Name}";
            _memoryCache.Remove(key);
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