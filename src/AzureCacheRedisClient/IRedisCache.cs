
namespace AzureCacheRedisClient
{
    public interface IRedisCache
    {
        /// <summary>
        /// Adds a value to Cache, only if no key with the same name exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        /// <returns>`true` if the value was set. `false` if the key already exists.</returns>
        Task<bool> Add(string key, object value, TimeSpan expiry);

        /// <summary>
        /// Returns a cache item by key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns>The value or null if no key exists</returns>
        Task<T?> Get<T>(string key);

        /// <summary>
        /// Sets a value in Cache, overwriting any existing value if same key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        Task Set(string key, object value, TimeSpan expiry);
    }
}