using StackExchange.Redis;
using System.Net.Sockets;
using System.Text.Json;

namespace AzureCacheRedisClient
{
    public class RedisCache
    {
        private readonly IDatabase _db;

        private int _retryMaxAttempts;

        public RedisCache(string connectionString)
        {
            _retryMaxAttempts = 5;
            Redis.InitializeConnectionString(connectionString);
            _db = HandleRedisExceptions(() => Redis.Connection.GetDatabase());
        }

        /// <summary>
        /// Adds a value to Cache, only if no key with the same name exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        /// <returns>`true` if the value was set. `false` if the key already exists.</returns>
        public async Task<bool> Add(string key, object value, TimeSpan expiry)
            => await HandleRedisExceptions(() => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.NotExists));

        /// <summary>
        /// Sets a value in Cache, overwriting any existing value if same key exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiry"></param>
        public async Task Set(string key, object value, TimeSpan expiry)
            => await HandleRedisExceptions(() => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.Always));

        /// <summary>
        /// Returns a cache item by key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns>The value or null if no key exists</returns>
        public async Task<T?> Get<T>(string key)
        {
            var value = await HandleRedisExceptions(() => _db.StringGetAsync(key));
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

        //private async Task<T> HandleRedisExceptionsAsync<T>(Func<Task<T>> func)
        //{
        //    int reconnectRetry = 0;
        //    int disposedRetry = 0;
        //    while (true)
        //    {
        //        try
        //        {
        //            return await func();
        //        }
        //        catch (Exception ex) when (ex is RedisConnectionException || ex is SocketException)
        //        {
        //            reconnectRetry++;
        //            if (reconnectRetry > _retryMaxAttempts)
        //                throw;
        //            Redis.ForceReconnect();
        //        }
        //        catch (ObjectDisposedException)
        //        {
        //            disposedRetry++;
        //            if (disposedRetry > _retryMaxAttempts)
        //                throw;
        //        }
        //    }
        //}
    }
}
