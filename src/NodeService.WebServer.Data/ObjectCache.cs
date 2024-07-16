using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Threading;

namespace NodeService.WebServer.Data
{
    public class ObjectCache
    {
        readonly IMemoryCache _memoryCache;
        readonly IDistributedCache _distributedCache;

        public ObjectCache(
            IMemoryCache memoryCache,
            IDistributedCache distributedCache)
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
        }

        public async ValueTask<T?> GetObjectAsync<T>(
            string key,
            CancellationToken cancellationToken = default)
        {
            var json = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (json == null)
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(json);
        }

        public async ValueTask SetObjectAsync<T>(
            string key,
            T value,
            CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(value);
            await _distributedCache.SetStringAsync(key, json, cancellationToken);
        }

        public async ValueTask SetEntityAsync<T>(
            T value,
            CancellationToken cancellationToken = default)
                        where T : EntityBase
        {
            var key = GetEntityKey(value);
            value.EntitySource = EntitySource.Cache;
            var json = JsonSerializer.Serialize(value);
            await _distributedCache.SetStringAsync(
                key,
                json,
                cancellationToken);
        }

        public async ValueTask<T?> GetEntityAsync<T>(
            string id,
            CancellationToken cancellationToken = default)
                where T : EntityBase
        {
            if (id == null)
            {
                id = string.Empty;
            }
            var key = GetEntityKey<T>(id);
            var entity = await GetObjectAsync<T>(key, cancellationToken);
            if (entity != null)
            {
                entity.EntitySource = EntitySource.Cache;
            }
            return entity;
        }

        public string GetEntityKey<T>(T obj)
            where T : EntityBase
        {
            return $"Object://{typeof(T).FullName}/{obj.Id}";
        }

        public string GetEntityKey<T>(string id)
    where T : EntityBase
        {
            return $"Object://{typeof(T).FullName}/{id}";
        }


        public async ValueTask RemoveObjectAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            await _distributedCache.RemoveAsync(key);
        }

        public async ValueTask RemoveEntityAsync<T>(
            T value,
            CancellationToken cancellationToken = default)
                where T : EntityBase
        {
            var key = GetEntityKey<T>(value.Id);
            await _distributedCache.RemoveAsync(key);
        }

        public async ValueTask RemoveEntityAsync<T>(
            string id,
            CancellationToken cancellationToken = default)
        where T : EntityBase
        {
            var key = GetEntityKey<T>(id);
            await _distributedCache.RemoveAsync(key);
        }


    }
}
