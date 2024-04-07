using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations.Schema;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public partial class ClientUpdateController : Controller
    {

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public ClientUpdateController(IDbContextFactory<ApplicationDbContext> dbContext)
        {
            this._dbContextFactory = dbContext;
        }

        [HttpGet("/api/clientupdate/getupdate")]
        public async Task<ApiResponse<ClientUpdateConfigModel>> GetUpdateAsync()
        {
            ApiResponse<ClientUpdateConfigModel> apiResponse = new ApiResponse<ClientUpdateConfigModel>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                apiResponse.Result = await
                    dbContext
                    .ClientUpdateConfigurationDbSet
                    .Where(static x => x.Status == ClientUpdateStatus.Public)
                    .OrderByDescending(static x => x.Version)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/clientupdate/addorupdate")]
        public async Task<ApiResponse> AddOrUpdateAsync([FromBody] ClientUpdateConfigModel model)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var modelFromDb = await dbContext.ClientUpdateConfigurationDbSet.FindAsync(model.Id);
                if (modelFromDb == null)
                {
                    await dbContext.ClientUpdateConfigurationDbSet.AddAsync(model);
                }
                else
                {
                    modelFromDb.Id = model.Id;
                    modelFromDb.Name = model.Name;
                    modelFromDb.Version = model.Version;
                    modelFromDb.DecompressionMethod = model.DecompressionMethod;
                    modelFromDb.DnsFilters = model.DnsFilters;
                    modelFromDb.DnsFilterType = model.DnsFilterType;
                    modelFromDb.PackageConfigId = model.PackageConfigId;
                    modelFromDb.Status = model.Status;
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpGet("/api/clientupdate/list")]
        public async Task<PaginationResponse<ClientUpdateConfigModel>> QueryClientUpdateListAsync()
        {
            PaginationResponse<ClientUpdateConfigModel> apiResponse = new PaginationResponse<ClientUpdateConfigModel>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                apiResponse.TotalCount = await dbContext.ClientUpdateConfigurationDbSet.CountAsync();
                apiResponse.Result = await dbContext.ClientUpdateConfigurationDbSet.OrderByDescending(x => x.Version).ToListAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/clientupdate/remove")]
        public async Task<ApiResponse> RemoveAsync([FromBody] ClientUpdateConfigModel model)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                dbContext.ClientUpdateConfigurationDbSet.Remove(model);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }


    }
}
