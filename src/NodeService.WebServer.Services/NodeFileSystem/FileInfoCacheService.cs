using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class FileInfoCacheService
    {
        readonly ConfigurationQueryService _configurationQueryService;
        readonly ObjectCache _objectCache;

        public FileInfoCacheService(
            ConfigurationQueryService configurationQueryService,
            ObjectCache objectCache)
        {
            _configurationQueryService = configurationQueryService;
            _objectCache = objectCache;
        }

        public async ValueTask<FileInfoCache> GetFileInfoCacheAsync(string configurationId, string storagePath, CancellationToken cancellationToken = default)
        {
            var ftpConfigList = await _configurationQueryService.QueryConfigurationByIdListAsync<FtpConfigModel>(
    [configurationId],
    cancellationToken);
            if (!ftpConfigList.HasValue)
            {
                throw new Exception("invalid ftp config id");
            }
            var ftpConfig = ftpConfigList.Items.FirstOrDefault();

            string fileCacheKey = NodeFileSyncHelper.BuiIdCacheKey(ftpConfig.Host, ftpConfig.Port, storagePath);
            var fileInfoCache = await _objectCache.GetObjectAsync<FileInfoCache>(fileCacheKey, cancellationToken);
            return fileInfoCache;
        }

    }
}
