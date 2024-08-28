using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.DataQuality;
using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class DataQualityController : Controller
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel>
        _nodeStatisticsRecordRepoFactory;

    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly IServiceProvider _serviceProvider;
    readonly ILogger<DataQualityController> _logger;
    readonly FileRecordQueryService _fileRecordQueryService;
    readonly IMemoryCache _memoryCache;


    public DataQualityController(
        ILogger<DataQualityController> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        FileRecordQueryService fileRecordQueryService,
        ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> nodeStatisticsRecordRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        IMemoryCache memoryCache)
    {
        _exceptionCounter = exceptionCounter;
        _memoryCache = memoryCache;
        _logger = logger;
        _fileRecordQueryService = fileRecordQueryService;
        _nodeStatisticsRecordRepoFactory = nodeStatisticsRecordRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _serviceProvider = serviceProvider;
    }
}