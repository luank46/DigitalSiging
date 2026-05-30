using System;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Async cache service interface (Redis-backed).
    /// Mapping từ IAsyncCacheService cũ.
    /// </summary>
    public interface IAsyncCacheService
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task ClearAsync();
        Task RemoveByPrefixAsync(string pattern);
        Task<TItem?> GetStringAsync<TItem>(string key);
        Task SetStringAsync<TItem>(string key, TItem value, int ttlSeconds);
    }
}
