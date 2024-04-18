using System.ComponentModel.DataAnnotations.Schema;

namespace NodeService.WebServer.Controllers
{
    public partial record class PackageConfigUploadModel : PackageConfigModel
    {
        [NotMapped]
        [JsonIgnore]
        public IFormFile? File { get; set; }
    }

    public partial class CommonConfigController
    {

        [HttpGet("/api/commonconfig/package/list")]
        public Task<PaginationResponse<PackageConfigModel>> QueryPackageConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<PackageConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/package/download/{packageId}")]
        public async Task<IActionResult> DownloadPackageAsync(string packageId)
        {
            ArgumentException.ThrowIfNullOrEmpty(packageId);
            using var dbContext = this._dbContextFactory.CreateDbContext();
            var model = await dbContext.GetDbSet<PackageConfigModel>().FindAsync(packageId);
            if (model == null)
            {
                return NotFound();
            }
            if (model.DownloadUrl == null)
            {
                return NotFound();
            }
            var fileContents = await this._memoryCache.GetOrCreateAsync<byte[]>(model.DownloadUrl, async () =>
            {
                using var stream = new MemoryStream();
                await this._virtualFileSystem.ConnectAsync();
                if (await this._virtualFileSystem.DownloadStream(model.DownloadUrl, stream))
                {
                    stream.Position = 0;
                    var hash = await CryptographyHelper.CalculateSHA256Async(stream);
                    stream.Position = 0;
                    if (hash != model.Hash)
                    {
                        return null;
                    }
                    if (!ZipArchiveHelper.TryRead(stream, out var zipArchive))
                    {
                        return null;
                    }
                    if (!zipArchive.Entries.Any(ZipArchiveHelper.HasPackageKey))
                    {
                        return null;
                    }
                    stream.Position = 0;
                }
                return stream.ToArray();
            }, TimeSpan.FromHours(1));
            if (fileContents == null)
            {
                return NotFound();
            }
            return File(fileContents, "application/octet-stream", model.FileName);
        }

        [HttpPost("/api/commonconfig/package/addorupdate")]
        [DisableRequestSizeLimit]
        public async Task<ApiResponse> AddOrUpdatePackageAsync([FromForm] PackageConfigUploadModel package)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                ArgumentNullException.ThrowIfNull(package.Name);
                ArgumentNullException.ThrowIfNull(package.Platform);
                ArgumentNullException.ThrowIfNull(package.Version);
                ArgumentNullException.ThrowIfNull(package.File);
                ArgumentNullException.ThrowIfNull(package.Hash);
                var fileName = Guid.NewGuid().ToString("N");
                var remotePath = Path.Combine(this._webServerOptions.GetPackagePath(package.Id), fileName);
                await this._virtualFileSystem.ConnectAsync();
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
                if (!await this._virtualFileSystem.UploadStream(remotePath, package.File.OpenReadStream()))
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "Upload stream fail";
                    return apiResponse;
                }
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var model = await dbContext.GetDbSet<PackageConfigModel>().FindAsync(package.Id);
                if (model == null)
                {
                    model = package;
                    await dbContext.GetDbSet<PackageConfigModel>().AddAsync(model);

                }
                else
                {
                    _memoryCache.Remove(model.DownloadUrl);
                    if (model.DownloadUrl != null)
                    {
                        await this._virtualFileSystem.DeleteFileAsync(model.DownloadUrl);
                    }
                    model.With(package);
                }
                model.FileName = package.File.FileName;
                model.FileSize = package.File.Length;
                model.DownloadUrl = remotePath;
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex.ToString());
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.ToString();
            }
            return apiResponse;
        }

        [HttpPost("/api/commonconfig/package/remove")]
        public Task<ApiResponse> DeletePackageConfigAsync([FromBody] PackageConfigModel model)
        {
            return RemoveConfigurationAsync(model);
        }


        [HttpGet("/api/commonconfig/package/{id}")]
        public Task<ApiResponse<PackageConfigModel>> QueryPackageConfigAsync(string id)
        {
            return QueryConfigurationAsync<PackageConfigModel>(id);
        }


    }
}
