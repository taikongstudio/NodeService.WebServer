using CommandLine;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public partial class NodesController : Controller
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        private readonly IAsyncQueue<JobScheduleMessage> _asyncQueue;
        private readonly INodeSessionService _nodeSessionService;
        private readonly IMemoryCache _memoryCache;
        private readonly IVirtualFileSystem _virtualFileSystem;
        private readonly WebServerOptions _webServerOptions;
        private readonly ILogger<NodesController> _logger;


        public NodesController(
            IMemoryCache memoryCache,
            IVirtualFileSystem virtualFileSystem,
            IOptionsSnapshot<WebServerOptions> webServerOptions,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<NodesController> logger,
            IAsyncQueue<JobScheduleMessage> jobScheduleServiceMessageQueue,
            INodeSessionService nodeSessionService)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _asyncQueue = jobScheduleServiceMessageQueue;
            _nodeSessionService = nodeSessionService;
            _memoryCache = memoryCache;
            _virtualFileSystem = virtualFileSystem;
            _webServerOptions = webServerOptions.Value;
        }

        [HttpGet("/api/nodes/list")]
        public async Task<ApiResponse<IEnumerable<NodeInfoModel>>> QueryNodeListAsync()
        {
            ApiResponse<IEnumerable<NodeInfoModel>> apiResponse = new ApiResponse<IEnumerable<NodeInfoModel>>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                apiResponse.Result =
                    await dbContext
                    .NodeInfoDbSet
                    .AsQueryable()
                    .Include(x => x.Profile)
                    .AsSplitQuery()
                    .OrderByDescending(x => x.Status)
                    .ThenByDescending(x => x.Profile.UpdateTime)
                    .ThenBy(x => x.Name)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.ToString();
            }
            return apiResponse;
        }

        [HttpGet("/api/nodes/{id}")]
        public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id)
        {
            ApiResponse<NodeInfoModel> apiResponse = new ApiResponse<NodeInfoModel>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var nodeInfo =
                    await dbContext
                    .NodeInfoDbSet
                    .FindAsync(id);
                apiResponse.Result = nodeInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.ToString();
            }
            return apiResponse;
        }


    }
}
