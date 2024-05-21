using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.RateLimiting;
using NodeService.WebServer.Data.Repositories;

namespace NodeService.WebServer.Controllers;

public record class PackageConfigUploadModel : PackageConfigModel
{
    [NotMapped] [JsonIgnore] public IFormFile? File { get; set; }
}

public partial class CommonConfigController
{
    [HttpGet("/api/CommonConfig/Package/List")]
    public Task<PaginationResponse<PackageConfigModel>> QueryPackageConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<PackageConfigModel>(queryParameters);
    }


    [HttpGet("/api/CommonConfig/Package/Download/{packageId}")]
    public async Task<IActionResult> DownloadPackageAsync(string packageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        using var virtualFileSystem = _serviceProvider.GetService<IVirtualFileSystem>();
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PackageConfigModel>>();
        using var repo = repoFactory.CreateRepository();
        var model = await repo.GetByIdAsync(packageId);
        if (model == null) return NotFound();
        if (model.DownloadUrl == null) return NotFound();
        var packageCacheKey = $"Package:{model.DownloadUrl}";
        var fileContents = await _memoryCache.GetOrCreateAsync(packageCacheKey, async () =>
        {
            using var stream = new MemoryStream();
            await virtualFileSystem.ConnectAsync();
            if (await virtualFileSystem.DownloadStream(model.DownloadUrl, stream))
            {
                stream.Position = 0;
                var hash = await CryptographyHelper.CalculateSHA256Async(stream);
                stream.Position = 0;
                if (hash != model.Hash) return null;
                if (!ZipArchiveHelper.TryRead(stream, out var zipArchive)) return null;
                if (!zipArchive.Entries.Any(ZipArchiveHelper.HasPackageKey)) return null;
                stream.Position = 0;
            }

            return stream.ToArray();
        }, TimeSpan.FromHours(1));
        if (fileContents == null) return NotFound();
        return File(fileContents, "application/octet-stream", model.FileName);
    }

    [HttpPost("/api/CommonConfig/Package/AddOrUpdate")]
    [DisableRequestSizeLimit]
    public async Task<ApiResponse> AddOrUpdatePackageAsync([FromForm] PackageConfigUploadModel package)
    {
        var apiResponse = new ApiResponse();
        try
        {
            using var virtualFileSystem = _serviceProvider.GetService<IVirtualFileSystem>();
            ArgumentNullException.ThrowIfNull(package.Name);
            ArgumentNullException.ThrowIfNull(package.Platform);
            ArgumentNullException.ThrowIfNull(package.Version);
            ArgumentNullException.ThrowIfNull(package.File);
            ArgumentNullException.ThrowIfNull(package.Hash);
            var fileName = Guid.NewGuid().ToString("N");
            var remotePath = Path.Combine(_webServerOptions.GetPackagePath(package.Id), fileName);
            await virtualFileSystem.ConnectAsync();
            var stream = package.File.OpenReadStream();
            if (!ZipArchiveHelper.TryRead(stream, out var zipArchive))
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "not a zip package";
                return apiResponse;
            }

            if (!zipArchive.Entries.Any(ZipArchiveHelper.HasPackageKey))
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "Invalid package";
                return apiResponse;
            }

            stream.Position = 0;
            if (!await virtualFileSystem.UploadStream(remotePath, stream))
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "Upload stream fail";
                return apiResponse;
            }

            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PackageConfigModel>>();
            using var repo = repoFactory.CreateRepository();
            var model = await repo.GetByIdAsync(package.Id);
            if (model == null)
            {
                model = package;
                await repo.AddAsync(model);
            }
            else
            {
                var packageCacheKey = $"Package:{model.DownloadUrl}";
                _memoryCache.Remove(packageCacheKey);
                if (model.DownloadUrl != null) await virtualFileSystem.DeleteFileAsync(model.DownloadUrl);
                model.With(package);
            }

            model.FileName = package.File.FileName;
            model.FileSize = package.File.Length;
            model.DownloadUrl = remotePath;
            await repo.SaveChangesAsync();
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

    [HttpPost("/api/CommonConfig/Package/Remove")]
    public Task<ApiResponse> DeletePackageConfigAsync([FromBody] PackageConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }


    [HttpGet("/api/CommonConfig/Package/{id}")]
    public Task<ApiResponse<PackageConfigModel>> QueryPackageConfigAsync(string id)
    {
        return QueryConfigurationAsync<PackageConfigModel>(id);
    }
}