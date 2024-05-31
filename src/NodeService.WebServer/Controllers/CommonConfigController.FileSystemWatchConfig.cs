namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/CommonConfig/filesystemwatch/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] FileSystemWatchConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/CommonConfig/filesystemwatch/List")]
        public Task<PaginationResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigurationListAsync(
            [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationListAsync<FileSystemWatchConfigModel>(queryParameters);
        }

        [HttpGet("/api/CommonConfig/filesystemwatch/{id}")]
        public Task<ApiResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigAsync(string id)
        {
            return QueryConfigurationAsync<FileSystemWatchConfigModel>(id);
        }


        [HttpPost("/api/CommonConfig/filesystemwatch/Remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] FileSystemWatchConfigModel model)
        {
            return DeleteConfigurationAsync(model);
        }


    }
}
