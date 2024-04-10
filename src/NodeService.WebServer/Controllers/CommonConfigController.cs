namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public partial class CommonConfigController : Controller
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly IVirtualFileSystem _virtualFileSystem;
        private readonly WebServerOptions _webServerOptions;
        private readonly ILogger<CommonConfigController> _logger;
        private readonly INodeSessionService _nodeSessionService;
        private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
        private readonly IServiceProvider _serviceProvider;

        public CommonConfigController(
            IMemoryCache memoryCache,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<CommonConfigController> logger,
            IVirtualFileSystem virtualFileSystem,
            IOptionsSnapshot<WebServerOptions> optionSnapshot,
            IServiceProvider serviceProvider,
            INodeSessionService nodeSessionService,
            IAsyncQueue<NotificationMessage> notificationMessageQueue)
        {
            _serviceProvider = serviceProvider;
            _dbContextFactory = dbContextFactory;
            _memoryCache = memoryCache;
            _virtualFileSystem = virtualFileSystem;
            _webServerOptions = optionSnapshot.Value;
            _logger = logger;
            _nodeSessionService = nodeSessionService;
            _notificationMessageQueue = notificationMessageQueue;
        }

        private async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(QueryParametersModel queryParameters)
            where T : ConfigurationModel, new()
        {
            PaginationResponse<T> apiResponse = new PaginationResponse<T>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                string name = typeof(T).Name;
                var queryable = dbContext.GetDbSet<T>()
                    .AsQueryable();

                apiResponse.PageIndex = queryParameters.PageIndex;
                apiResponse.PageSize = queryParameters.PageSize;

                apiResponse.TotalCount = await queryable.CountAsync();
                if (queryParameters.PageSize == -1 && queryParameters.PageIndex == -1)
                {
                    apiResponse.Result = await
                        queryable.OrderByDescending(x => x.ModifiedDateTime)
                        .ToListAsync();
                }
                else
                {
                    apiResponse.Result = await
                        queryable.OrderByDescending(x => x.ModifiedDateTime)
                        .Skip(queryParameters.PageSize * queryParameters.PageIndex)
                        .Take(queryParameters.PageSize)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }

            return apiResponse;
        }

        private async Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, Task>? func = null)
                        where T : ConfigurationModel
        {
            ApiResponse<T> apiResponse = new ApiResponse<T>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                apiResponse.Result = await dbContext.GetDbSet<T>().AsQueryable().FirstOrDefaultAsync(x => x.Id == id);
                if (apiResponse.Result != null && func != null)
                {
                    await func.Invoke(apiResponse.Result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }

            return apiResponse;
        }

        private async Task<ApiResponse> RemoveConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
                             where T : ConfigurationModel
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                dbContext.GetDbSet<T>().Remove(model);
                int changes = await dbContext.SaveChangesAsync();
                if (changes > 0 && changesFunc != null)
                {
                    await changesFunc(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }

            return apiResponse;
        }

        private async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
                               where T : ConfigurationModel
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var modelFromDb = await dbContext.GetDbSet<T>().FindAsync(model.Id);
                if (modelFromDb == null)
                {
                    model.EntityVersion = Guid.NewGuid().ToByteArray();
                    await dbContext
                        .GetDbSet<T>().AddAsync(model);
                }
                else
                {
                    modelFromDb.With(model);
                    modelFromDb.EntityVersion = Guid.NewGuid().ToByteArray();
                }
                int changes = await dbContext.SaveChangesAsync();
                if (changes > 0 && changesFunc != null)
                {
                    await changesFunc.Invoke(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }

            return apiResponse;
        }

    }
}
