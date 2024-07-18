﻿using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.DataQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
