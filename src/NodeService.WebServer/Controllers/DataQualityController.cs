using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQuality;
using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class DataQualityController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _insertUpdateDeleteOpBatchQueue;

    private readonly ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel>
        _nodeStatisticsRecordRepoFactory;

    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataQualityController> _logger;
    private readonly IMemoryCache _memoryCache;

    private readonly BatchQueue<BatchQueueOperation<FileRecordBatchQueryParameters, ListQueryResult<FileRecordModel>>>
        _queryOpBatchQueue;

    public DataQualityController(
        ILogger<DataQualityController> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        BatchQueue<BatchQueueOperation<FileRecordBatchQueryParameters, ListQueryResult<FileRecordModel>>>
            queryOpBatchQueue,
        BatchQueue<BatchQueueOperation<FileRecordModel, bool>> insertUpdateDeleteOpBatchQueue,
        ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> nodeStatisticsRecordRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache memoryCache)
    {
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
        _memoryCache = memoryCache;
        _logger = logger;
        _queryOpBatchQueue = queryOpBatchQueue;
        _insertUpdateDeleteOpBatchQueue = insertUpdateDeleteOpBatchQueue;
        _nodeStatisticsRecordRepoFactory = nodeStatisticsRecordRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _serviceProvider = serviceProvider;
    }
}