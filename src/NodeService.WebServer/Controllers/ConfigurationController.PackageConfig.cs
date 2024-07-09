using System.ComponentModel.DataAnnotations.Schema;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Security.Policy;
using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.VirtualFileSystem;

namespace NodeService.WebServer.Controllers;

public record class PackageConfigUploadModel : PackageConfigModel
{
    [NotMapped] [JsonIgnore] public IFormFile? File { get; set; }
}

public partial class ConfigurationController
{
    [HttpGet("/api/Configuration/Package/List")]
    public Task<PaginationResponse<PackageConfigModel>> QueryPackageConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<PackageConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Package/Download/{packageId}")]
    [HttpGet("/api/Configuration/Package/Download/{packageId}")]
    public async Task<IActionResult> DownloadPackageAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        var paramters = new PackageDownloadParameters(packageId);
        var op = new BatchQueueOperation<PackageDownloadParameters, PackageDownloadResult>(
            paramters,
            BatchQueueOperationKind.Query,
             BatchQueueOperationPriority.Normal);
        var queue = _serviceProvider.GetService<BatchQueue<BatchQueueOperation<PackageDownloadParameters, PackageDownloadResult>>>();
        await queue.SendAsync(op);
        var serviceResult = await op.WaitAsync(cancellationToken);
        if (serviceResult.Contents == null)
        {
            return NotFound();
        }
        return File(serviceResult.Contents, "application/octet-stream", packageId);
    }

    [HttpPost("/api/Configuration/Package/AddOrUpdate")]
    [DisableRequestSizeLimit]
    public async Task<ApiResponse> AddOrUpdatePackageAsync(
        [FromForm] PackageConfigUploadModel uploadPackage,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();
        var queryRsp = await QueryPackageConfigAsync(uploadPackage.Id, cancellationToken);
        if (apiResponse.ErrorCode != 0)
        {
            return queryRsp;
        }
        var package = queryRsp.Result;
        apiResponse = await UploadFileAsync(uploadPackage, package, cancellationToken);
        if (apiResponse.ErrorCode == 0)
        {
            apiResponse = await AddOrUpdateConfigurationAsync<PackageConfigModel>(
                                 uploadPackage,
                                 OnPackageConfigurationChanged,
                                 cancellationToken: cancellationToken);
        }
        return apiResponse;
    }

    async ValueTask<ApiResponse> UploadFileAsync(
        PackageConfigUploadModel uploadPackage,
        PackageConfigModel? packageConfig,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();
        try
        {
            ArgumentNullException.ThrowIfNull(uploadPackage.Name);
            ArgumentNullException.ThrowIfNull(uploadPackage.Platform);
            ArgumentNullException.ThrowIfNull(uploadPackage.Version);
            ArgumentNullException.ThrowIfNull(uploadPackage.Hash);
            if (uploadPackage.File != null)
            {
                var stream = uploadPackage.File.OpenReadStream();
                using var memoryStream = new MemoryStream((int)stream.Length);
                await stream.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Seek(0, SeekOrigin.Begin);
                var fileHashBytes = await SHA256.HashDataAsync(memoryStream, cancellationToken);
                var hash = BitConverter.ToString(fileHashBytes).Replace("-", "").ToLowerInvariant();
                if (packageConfig != null && packageConfig.Hash == hash)
                {
                    uploadPackage.DownloadUrl = packageConfig.DownloadUrl;
                    return apiResponse;
                }

                using var virtualFileSystem = _serviceProvider.GetService<IVirtualFileSystem>();
                var remotePath = Path.Combine(_webServerOptions.GetPackagePath(uploadPackage.Id), hash);
                await virtualFileSystem.ConnectAsync(cancellationToken);

                if (!ZipArchiveHelper.TryRead(stream, out var zipArchive))
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "not a zip package";
                }

                if (!zipArchive.Entries.Any(ZipArchiveHelper.HasPackageKey))
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "Invalid package";
                    return apiResponse;
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                if (!await virtualFileSystem.UploadStream(remotePath, memoryStream, cancellationToken: cancellationToken))
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "Upload stream fail";
                    return apiResponse;
                }
                uploadPackage.DownloadUrl = remotePath;
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }

    [HttpPost("/api/Configuration/Package/Remove")]
    public Task<ApiResponse> DeletePackageConfigAsync(
        [FromBody] PackageConfigModel model,
        CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(
            model,
            OnPackageConfigurationChanged,
            cancellationToken: cancellationToken);
    }


    [HttpGet("/api/Configuration/Package/{id}")]
    public Task<ApiResponse<PackageConfigModel>> QueryPackageConfigAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<PackageConfigModel>(
            id,
            cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/Package/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryPackageConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(
            queryParameters,
            cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/Package/SwitchVersion")]
    public Task<ApiResponse> SwitchPackageConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters,
        CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<PackageConfigModel>(
            parameters,
            OnPackageConfigurationChanged,
            cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/Package/DeleteVersion")]
    public Task<ApiResponse> DeletePackageConfigurationVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity,
        CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<PackageConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }

    ValueTask OnPackageConfigurationChanged(
        ConfigurationSaveChangesResult result,
        CancellationToken cancellationToken = default)
    {
        switch (result.Type)
        {
            case ConfigurationChangedType.None:
                break;
            case ConfigurationChangedType.Add:
            case ConfigurationChangedType.Update:
                {
                    if (result.OldValue is PackageConfigModel oldPackage)
                    {
                        var packageCacheKey = $"Package:{oldPackage.Id}";
                        _memoryCache.Remove(packageCacheKey);
                    }
                }
                break;
            case ConfigurationChangedType.Delete:
                {
                    if (result.OldValue is PackageConfigModel oldPackage)
                    {
                        var packageCacheKey = $"Package:{oldPackage.Id}";
                        _memoryCache.Remove(packageCacheKey);
                    }
                }

                break;
            default:
                break;
        }

        return ValueTask.CompletedTask;
    }
}