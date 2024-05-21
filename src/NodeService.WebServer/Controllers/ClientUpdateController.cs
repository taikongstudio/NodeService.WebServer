using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ClientUpdateController : Controller
{
    private readonly ApplicationRepositoryFactory<ClientUpdateCounterModel> _clientUpdateCounterRepoFactory;
    private readonly ApplicationRepositoryFactory<ClientUpdateConfigModel> _clientUpdateRepoFactory;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<ClientUpdateController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;

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

    [HttpGet("/api/clientupdate/GetUpdate")]
    public async Task<ApiResponse<ClientUpdateConfigModel>> GetUpdateAsync([FromQuery] string? name)
    {
        var apiResponse = new ApiResponse<ClientUpdateConfigModel>();
        try
        {
            ClientUpdateConfigModel? clientUpdateConfig = null;
            if (string.IsNullOrEmpty(name)) return apiResponse;

            var ipAddress = HttpContext.Connection.RemoteIpAddress.ToString();
            var updateKey = $"ClientUpdateEnabled:{ipAddress}";
            if (!_memoryCache.TryGetValue<bool>(updateKey, out var isEnabled))
            {
                return apiResponse;
            }



            var key = $"ClientUpdateConfig:{name}";
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 20)));
            if (!_memoryCache.TryGetValue<ClientUpdateConfigModel>(key, out var cacheValue) || cacheValue == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 20)));
                if (!_memoryCache.TryGetValue(key, out cacheValue) || cacheValue == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(10, 20)));
                    await using var repo = _clientUpdateRepoFactory.CreateRepository();

                    cacheValue =
                        await repo.FirstOrDefaultAsync(
                            new ClientUpdateConfigSpecification(ClientUpdateStatus.Public, name));

                    if (cacheValue != null) _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(1));
                }
            }

            if (cacheValue != null) clientUpdateConfig = cacheValue;


            if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null &&
                clientUpdateConfig.DnsFilters.Any())
            {
                await using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();

                var dataFilterType = DataFilterTypes.None;

                if (clientUpdateConfig.DnsFilterType.Equals("include", StringComparison.OrdinalIgnoreCase))
                    dataFilterType = DataFilterTypes.Include;

                else if (clientUpdateConfig.DnsFilterType.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                    dataFilterType = DataFilterTypes.Exclude;
                var nodeList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    default,
                    new DataFilterCollection<string>(
                        dataFilterType,
                        clientUpdateConfig.DnsFilters.Select(x => x.Value))));
                if (nodeList.Count != 0)
                {
                    if (dataFilterType == DataFilterTypes.Include)
                        apiResponse.SetResult(clientUpdateConfig);
                    else if (dataFilterType == DataFilterTypes.Exclude) apiResponse.SetResult(null);
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

    [HttpPost("/api/clientupdate/AddOrUpdate")]
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
                modelFromRepo.Id = model.Id;
                modelFromRepo.Name = model.Name;
                modelFromRepo.Version = model.Version;
                modelFromRepo.DecompressionMethod = model.DecompressionMethod;
                modelFromRepo.DnsFilters = model.DnsFilters;
                modelFromRepo.DnsFilterType = model.DnsFilterType;
                modelFromRepo.PackageConfigId = model.PackageConfigId;
                modelFromRepo.Status = model.Status;
                await repo.SaveChangesAsync();
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

    [HttpGet("/api/clientupdate/List")]
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


    [HttpPost("/api/clientupdate/Remove")]
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