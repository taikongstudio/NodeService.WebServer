namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/FileSystemWatch/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FileSystemWatchConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/FileSystemWatch/List")]
    public Task<PaginationResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FileSystemWatchConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/FileSystemWatch/{id}")]
    public Task<ApiResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FileSystemWatchConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/FileSystemWatch/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FileSystemWatchConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }
}