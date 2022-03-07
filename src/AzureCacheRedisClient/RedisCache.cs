using StackExchange.Redis;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace AzureCacheRedisClient
{
    public class RedisCache : IRedisCache
    {
        private IDatabase? _db;
        private TelemetryClient? _telemetryClient;

        private const int _retryMaxAttempts = 5;

        private string? _serverHostname;

        public RedisCache()
        {            
        }

        public RedisCache(string connectionString)
        {
            Connect(connectionString);
        }

        public RedisCache(TelemetryClient telemetryClient) => _telemetryClient = telemetryClient;

        public RedisCache(string connectionString, TelemetryClient telemetryClient) : this(connectionString) => _telemetryClient = telemetryClient;

        /// <summary>
        /// Indicates if a connection has been initialized and an active connection has been made to a Redis database. 
        /// Once true, always true, i.e. connection failures will not reset this property to false.
        /// </summary>
        public bool IsConnected { get { return _db != null; } }

        public TelemetryClient? TelemetryClient { get => _telemetryClient; set => _telemetryClient = value; }

        /// <summary>
        /// Initialize an active connection to Redis.
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connect(string connectionString)
        {
            Redis.InitializeConnectionString(connectionString);
            _db = HandleRedis("Connection Get Db", null, () => Redis.Connection.GetDatabase());
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
        /// Handles Redis exceptions and Tracks dependency calls via Application Insights
        /// </summary>
        /// <remarks>https://docs.microsoft.com/en-us/azure/azure-cache-for-redis/cache-dotnet-how-to-use-azure-redis-cache</remarks>
        private T HandleRedis<T>(string operation, string? data, Func<T> func)
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

                    reconnectRetry++;
                    if (reconnectRetry > _retryMaxAttempts)
                        throw;
                    Redis.ForceReconnect();
                }
                catch (ObjectDisposedException ex)
                {
                    TrackDependency(telemetry, ex);

                    disposedRetry++;
                    if (disposedRetry > _retryMaxAttempts)
                        throw;
                }
                catch (Exception ex)
                {
                    TrackDependency(telemetry, ex);
                }
            }
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
