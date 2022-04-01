using StackExchange.Redis;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AzureCacheRedisClientTests")]

namespace AzureCacheRedisClient
{
    /// <summary>
    /// Provides a set of operations on a Redis Db that are commonly used for Caching.
    /// </summary>
    public class RedisDb : IRedisCache, IRedisDb
    {
        private IDatabase? _db;
        private TelemetryClient? _telemetryClient;

        private const int _retryMaxAttempts = 5;

        private string? _serverHostname;

        public RedisDb()
        {            
        }

        public RedisDb(string connectionString)
        {
            Connect(connectionString);
        }

        public RedisDb(TelemetryClient telemetryClient) => _telemetryClient = telemetryClient;

        public RedisDb(string connectionString, TelemetryClient telemetryClient) : this(connectionString) => _telemetryClient = telemetryClient;

        /// <summary>
        /// Indicates if a connection has been initialized and an active connection has been made to a Redis database. 
        /// Once true, always true, i.e. connection failures will not reset this property to false.
        /// </summary>
        public bool IsConnected { get { return _db != null; } }

        public TelemetryClient? TelemetryClient { get => _telemetryClient; set => _telemetryClient = value; }

        internal static int RetryMaxAttempts => _retryMaxAttempts;

        /// <summary>
        /// Initialize an active connection to Redis.
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connect(string connectionString)
        {
            RedisConnection.InitializeConnectionString(connectionString);
            _db = HandleRedis("Connection Get Db", null, () => RedisConnection.Connection.GetDatabase());
            _serverHostname = ServerHostName(connectionString);
        }

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

            return await HandleRedis("Db Add", key, () => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.NotExists));
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

            var value = await HandleRedis("Db Get", key, () => _db.StringGetAsync(key));

            if (value.IsNull) return default;
            return JsonSerializer.Deserialize<T>(value);
        }

        /// <summary>
        /// Increments the <see cref="long"/> number stored at <paramref name="key"/> by <paramref name="increment"/> . 
        /// If the key does not exist, it is set to 0 before performing the operation.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="increment">The amount to increment by (defaults to 1).</param>
        /// <param name="fireAndForget">When true, the caller will immediately receive a default-value. This value is not indicative of anything at the server.</param>
        public async Task<long> Increment(string key, long increment = 1, bool fireAndForget = false)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");

            return await HandleRedis("Db Increment", key, () => _db.StringIncrementAsync(key, increment, fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None));
        }

        /// <summary>
        /// Decrements the <see cref="long"/> number stored at <paramref name="key"/> by <paramref name="decrement"/> .
        /// If the key does not exist, it is set to 0 before performing the operation.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="increment">The amount to decrement by (defaults to 1).</param>
        /// <param name="fireAndForget">When true, the caller will immediately receive a default-value. This value is not indicative of anything at the server.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<long> Decrement(string key, long decrement = 1, bool fireAndForget = false)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");

            return await HandleRedis("Db Decrement", key, () => _db.StringDecrementAsync(key, decrement, fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None));
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

            await HandleRedis("Db Set", key, () => _db.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), expiry, When.Always));
        }

        /// <summary>
        /// Removes the specified key. If the key does not exist, it is ignored.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="fireAndForget"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task Delete(string key, bool fireAndForget = false)
        {
            if (_db == null) throw new InvalidOperationException("Redis Database is not connected. Call Connect(connectionString).");

            await HandleRedis("Db Del", key, () => _db.KeyDeleteAsync(key, fireAndForget ? CommandFlags.FireAndForget : CommandFlags.None));
        }


        /// <summary>
        /// Handles Redis exceptions and Tracks dependency calls via Application Insights
        /// </summary>
        /// <remarks>https://docs.microsoft.com/en-us/azure/azure-cache-for-redis/cache-dotnet-how-to-use-azure-redis-cache</remarks>
        internal T HandleRedis<T>(string operation, string? data, Func<T> func)
        {
            int reconnectRetry = 0;
            int disposedRetry = 0;
            while (true)
            {
                var telemetry = NewDependencyTelemetry(operation, data);

                try
                {
                    var result = func();
                    TrackDependency(telemetry);
                    return result;
                }
                catch (Exception ex) when (ex is RedisConnectionException || ex is SocketException)
                {
                    TrackDependency(telemetry, ex);
                    TrackException(ex);

                    reconnectRetry++;
                    if (reconnectRetry > _retryMaxAttempts)
                    {
                        TrackTrace($"RedisCache: Throwing after {reconnectRetry} connection errors.", SeverityLevel.Error);
                        throw;
                    }

                    TrackTrace($"RedisCache: Force reconnect after {reconnectRetry} connection errors.", SeverityLevel.Warning);
                    RedisConnection.ForceReconnect();
                }
                catch (ObjectDisposedException ex)
                {
                    TrackDependency(telemetry, ex);
                    TrackException(ex);

                    disposedRetry++;
                    if (disposedRetry > _retryMaxAttempts)
                    {
                        TrackTrace($"RedisCache: Throwing after {disposedRetry} Object Disposed Exceptions.", SeverityLevel.Error);
                        throw;
                    }

                    TrackTrace($"RedisCache: Retrying after {disposedRetry} Object Disposed Exceptions.", SeverityLevel.Warning);
                }
                catch (Exception ex)
                {
                    TrackDependency(telemetry, ex);
                    TrackException(ex);
                    throw;
                }
            }
        }

        private void TrackTrace(string message, SeverityLevel severity)
        {
            if (_telemetryClient != null) _telemetryClient.TrackTrace(message, severity);
        }

        private void TrackException(Exception ex)
        {
            if (_telemetryClient != null) _telemetryClient.TrackException(ex);
        }

        private void TrackDependency(DependencyTelemetry telemetry, Exception? ex = null)
        {
            if (_telemetryClient != null)
            {
                telemetry.Duration = DateTimeOffset.Now.Subtract(telemetry.Timestamp);
                telemetry.Success = ex == null;
                _telemetryClient.TrackDependency(telemetry);
                if (ex != null) _telemetryClient.TrackException(ex);
            }
        }

        private static string ServerHostName(string? connectionString)
        {
            if (connectionString == null) return string.Empty;
            string[] dotParts = connectionString.Split('.');
            if (dotParts.Length < 2) return connectionString;
            return dotParts[0];
        }

        private DependencyTelemetry NewDependencyTelemetry(string operation, string? data) => new DependencyTelemetry
        {
            Data = data,
            Name = operation,
            Target = _serverHostname,
            Type = "Azure Cache Redis",
            Timestamp = DateTimeOffset.Now,
        };

    }
}
