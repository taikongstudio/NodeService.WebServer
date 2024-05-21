using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.FileRecords;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class DataQualityController : Controller
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _insertUpdateDeleteOpBatchQueue;
    readonly ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> _nodeStatisticsRecordRepoFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ILogger<DataQualityController> _logger;
    readonly IMemoryCache _memoryCache;

    private readonly BatchQueue<BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>>
        _queryOpBatchQueue;

    public DataQualityController(
        ILogger<DataQualityController> logger,
        ExceptionCounter exceptionCounter,
        [FromKeyedServices(nameof(FileRecordQueryService))]
        BatchQueue<BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>> queryOpBatchQueue,
        [FromKeyedServices(nameof(FileRecordInsertUpdateDeleteService))]
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
    }


}