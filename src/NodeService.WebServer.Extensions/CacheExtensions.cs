using Microsoft.Extensions.Caching.Memory;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NodeService.WebServer.Extensions
{
    public static class CacheExtensions
    {
        public static async Task<T?> GetOrCreateAsync<T>(this IMemoryCache memoryCache,
            string key,
            TimeSpan absoluteExpirationRelativeToNow)
            where T : new()
        {
            var value = await memoryCache.GetOrCreateAsync(key, async (cacheEntry) =>
            {
                var value = new T();
                cacheEntry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
                return value;
            });
            return value;
        }

        public static async Task<T?> GetOrCreateAsync<T>(this IMemoryCache memoryCache,
            string key,
            Func<Task<T?>> func,
            TimeSpan absoluteExpirationRelativeToNow)
        {
            var value = await memoryCache.GetOrCreateAsync(key, async (cacheEntry) =>
            {
                var value = await func();
                cacheEntry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
                return value;
            });
            return value;
        }

    }
}
