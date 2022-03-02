using StackExchange.Redis;
using System.Net.Sockets;
using System.Text.Json;

namespace AzureCacheRedisClient
{
    public class RedisCache : IRedisCache
    {
        private IDatabase? _db;

        private const int _retryMaxAttempts = 5;

        public RedisCache()
        {            
        }

        public RedisCache(string connectionString)
        {
            Connect(connectionString);
        }

        /// <summary>
        /// Initialize an active connection to Redis.
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connect(string connectionString)
        {
            Redis.InitializeConnectionString(connectionString);
            _db = HandleRedisExceptions(() => Redis.Connection.GetDatabase());
        }

        /// <summary>
        /// Indicates if a connection has been initialized and an active connection has been made to a Redis database. 
        /// Once true, always true, i.e. connection failures will not reset this property to false.
        /// </summary>
        public bool IsConnected { get { return _db != null; } }

        /// <summary>
        /// Adds a value to Cache, only if no key with the same name exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        /// <returns>`true` if the value was set. `false` if the key already exists.</returns>
        public async Task<bool> Add(string key, object value, TimeSpan expiry)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");
            return await HandleRedisExceptions(() => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.NotExists));
        }

        /// <summary>
        /// Sets a value in Cache, overwriting any existing value if same key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        public async Task Set(string key, object value, TimeSpan expiry)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");
            await HandleRedisExceptions(() => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.Always));
        }

        /// <summary>
        /// Returns a cache item by key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns>The value or default if no key exists</returns>
        public async Task<T?> Get<T>(string key)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");
            var value = await HandleRedisExceptions(() => _db.StringGetAsync(key));
            if (value.IsNull) return default;
            return JsonSerializer.Deserialize<T>(value);
        }

        // https://docs.microsoft.com/en-us/azure/azure-cache-for-redis/cache-dotnet-how-to-use-azure-redis-cache
        private T HandleRedisExceptions<T>(Func<T> func)
        {
            int reconnectRetry = 0;
            int disposedRetry = 0;
            while (true)
            {
                try
                {
                    return func();
                }
                catch (Exception ex) when (ex is RedisConnectionException || ex is SocketException)
                {
                    reconnectRetry++;
                    if (reconnectRetry > _retryMaxAttempts)
                        throw;
                    Redis.ForceReconnect();
                }
                catch (ObjectDisposedException)
                {
                    disposedRetry++;
                    if (disposedRetry > _retryMaxAttempts)
                        throw;
                }
            }
        }
    }
}
