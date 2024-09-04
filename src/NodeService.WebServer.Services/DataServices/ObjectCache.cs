using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using System.Threading;

namespace NodeService.WebServer.Services.DataServices
{
    public class ObjectCache
    {
        readonly IMemoryCache _memoryCache;
        readonly IDistributedCache _distributedCache;
        readonly ILogger<ObjectCache> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly RedisOptions _redisOptions;

        public ObjectCache(
            ILogger<ObjectCache> logger,
            ExceptionCounter exceptionCounter,
            IMemoryCache memoryCache,
            IDistributedCache distributedCache,
            IOptionsMonitor<RedisOptions> redisOptionsMonitor
            )
        {
            _memoryCache = memoryCache;
            _distributedCache = distributedCache;
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _redisOptions = redisOptionsMonitor.CurrentValue;
        }

        public async ValueTask<T?> GetObjectAsync<T>(
            string key,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var json = await _distributedCache.GetStringAsync(key, cancellationToken);
                if (json == null)
                {
                    return default;
                }
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            return default;
        }

        public async ValueTask<bool> SetObjectAsync<T>(
            string key,
            T value,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                await _distributedCache.SetStringAsync(key, json, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            return false;
        }

        public async ValueTask<bool> SetEntityAsync<T>(
            T value,
            CancellationToken cancellationToken = default)
                        where T : EntityBase
        {
            var key = GetEntityKey(value);
            return await SetObjectAsync(
                key,
                value,
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


        public async ValueTask<bool> RemoveObjectAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await _distributedCache.RemoveAsync(key, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            return false;

        }

        public async ValueTask<bool> RemoveEntityAsync<T>(
            T value,
            CancellationToken cancellationToken = default)
                where T : EntityBase
        {
            var key = GetEntityKey<T>(value.Id);
            return await RemoveObjectAsync(key, cancellationToken);
        }

        public async ValueTask<bool> RemoveEntityAsync<T>(
            string id,
            CancellationToken cancellationToken = default)
        where T : EntityBase
        {
            var key = GetEntityKey<T>(id);
            return await RemoveObjectAsync(key, cancellationToken);
        }


    }
}
