using Microsoft.Extensions.Caching.Memory;

namespace NodeService.WebServer.Extensions;

public static class CacheExtensions
{
    public static T? GetOrCreate<T>(this IMemoryCache memoryCache,
        string key,
        TimeSpan absoluteExpirationRelativeToNow)
        where T : new()
    {
        var value = memoryCache.GetOrCreate(key, cacheEntry =>
        {
            var value = new T();
            cacheEntry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
            return value;
        });
        return value;
    }

    public static async ValueTask<T?> GetOrCreateAsync<T>(this IMemoryCache memoryCache,
        string key,
        Func<ValueTask<T?>> func,
        TimeSpan absoluteExpirationRelativeToNow)
    {
        var value = await memoryCache.GetOrCreateAsync(key, async cacheEntry =>
        {
            var value = await func();
            cacheEntry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
            return value;
        });
        return value;
    }
}