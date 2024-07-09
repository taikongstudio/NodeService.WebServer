namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/filesystemwatch/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FileSystemWatchConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/filesystemwatch/List")]
    public Task<PaginationResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FileSystemWatchConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/filesystemwatch/{id}")]
    public Task<ApiResponse<FileSystemWatchConfigModel>> QueryFileSystemWatchConfigModelConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FileSystemWatchConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/Configuration/filesystemwatch/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FileSystemWatchConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }
}