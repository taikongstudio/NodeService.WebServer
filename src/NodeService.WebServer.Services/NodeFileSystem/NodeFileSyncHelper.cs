using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class NodeFileSyncHelper
    {
        public static string GetNodeFilePath(string host, string filePath)
        {
            return $"NodeFileSystem://{host}/{filePath}";
        }

        public static string GetNodeFilePathHash(string nodeFilePath)
        {
            var fileIdHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(nodeFilePath));
            var nodeFilePathHash = BitConverter.ToString(fileIdHashBytes).ToLowerInvariant();
            return nodeFilePathHash;
        }

        public static string BuiIdCacheKey(string host, int port, string storagePath)
        {
            return $"NodeFileSync://{host}:{port}/{storagePath}";
        }

        public static async ValueTask AddOrUpdateFileInfoAsync(
            this IDistributedCache distributedCache,
            string host,
            int port,
            string storagePath,
            string fullName,
            DateTime lastWriteTime,
            long length,
            CancellationToken cancellationToken = default)
        {
            var fileCacheKey = NodeFileSyncHelper.BuiIdCacheKey(
           host,
            port,
             storagePath);
            var cache = new FileInfoCache()
            {
                CreateDateTime = DateTime.UtcNow,
                DateTime = lastWriteTime,
                FullName = fullName,
                Length = length,
                ModifiedDateTime = DateTime.UtcNow
            };
            var cacheString = JsonSerializer.Serialize(cache);
            await distributedCache.SetStringAsync(fileCacheKey, cacheString, cancellationToken);
        }


    }
}
