using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public static class NodeFileSystemHelper
    {
       public   const string TempDirectory = "../NodeFileSystem/DecompressionTemp";

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

        public static async ValueTask<bool> HittestAsync(
            IServiceProvider serviceProvider,
            string nodeInfoId,
            NodeFileInfo nodeFileInfo)
        {
            var inMemoryDbContextFactory = serviceProvider.GetService<IDbContextFactory<InMemoryDbContext>>();
            using var inMemoryDbContext = inMemoryDbContextFactory.CreateDbContext();
            var nodeFileName = nodeFileInfo.FullName;
            var length = nodeFileInfo.Length;
            var lastWriteTime = nodeFileInfo.LastWriteTime;
            var exists = await inMemoryDbContext.NodeFileHittestResultCacheDbSet.Where(x => x.NodeInfoId == nodeInfoId && x.FullName == nodeFileName && x.Length == length && x.DateTime == lastWriteTime).AnyAsync();
            return exists;
        }

        public static async ValueTask<int> DeleteHittestResultCacheAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        {
            var inMemoryDbContextFactory = serviceProvider.GetService<IDbContextFactory<InMemoryDbContext>>();
            using var inMemoryDbContext = inMemoryDbContextFactory.CreateDbContext();
            var count = await inMemoryDbContext.NodeFileHittestResultCacheDbSet.Where(x => true).ExecuteDeleteAsync(cancellationToken);
            return count;
        }


    }
}
