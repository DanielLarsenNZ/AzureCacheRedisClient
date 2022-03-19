
namespace AzureCacheRedisClient
{
    /// <summary>
    /// Defines a set of operations on a Redis Db that are commonly used for Caching.
    /// </summary>

    public interface IRedisCache
    {
        /// <summary>
        /// Initialize an active connection to Redis.
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connect(string connectionString);

        /// <summary>
        /// Indicates if a connection has been initialized and an active connection has been made to a Redis database. 
        /// Once true, always true, i.e. connection failures will not reset this property to false.
        /// </summary>
        public bool IsConnected { get; }

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

        /// <summary>
        /// Deletes the specified key from the cache. A key is ignored if it does not exist. 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="fireAndForget"></param>
        /// <returns></returns>
        Task Delete(string key, bool fireAndForget = false);
    }
}