using AzureCacheRedisClient;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AzureCacheRedisClientTests
{
    [TestClass]
    public class RedisCacheTests
    {
        private readonly IConfiguration _configuration;
        private readonly TelemetryClient _telemetry;
        private readonly IOperationHolder<RequestTelemetry> _operation;

        public RedisCacheTests()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(@"appsettings.json", false, false)
                .AddJsonFile(@"appsettings.Development.json", true, false)
                .Build();

            _telemetry = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            _telemetry.InstrumentationKey = _configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];

            var request = new RequestTelemetry { Name = nameof(RedisCacheTests) };
            request.Context.Operation.Id = Guid.NewGuid().ToString("N");
            request.Context.Operation.ParentId = Guid.NewGuid().ToString("N");

            _operation = _telemetry.StartOperation(request);
        }

        public void Teardown()
        {

        }

        ~RedisCacheTests()
        {
            _telemetry.StopOperation(_operation);
            _telemetry.Flush();
        }

        [TestMethod]
        public async Task UsageTest()
        {
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            var item = new TestItem { Name = "foo", Value = "bar" };

            await cache.Set("foobar1", item, TimeSpan.FromSeconds(1));
            var cachedItem = await cache.Get<TestItem>("foobar1");

            Assert.AreEqual(item, cachedItem);
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void HandleRedis_ThrowObjectDisposedException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowObjectDisposedException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"], _telemetry);
            
            try
            {
                cache.HandleRedis<bool>(testName, null, () =>
                {
                    tries++;
                    throw new ObjectDisposedException(testName);
                });
            }
            catch (Exception)
            {
                Assert.IsTrue(tries > RedisCache.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisCache.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(RedisConnectionException))]
        public void HandleRedis_ThrowRedisConnectionException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowRedisConnectionException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            try
            {
                cache.HandleRedis<bool>(testName, null, () =>
                {
                    tries++;
                    throw new RedisConnectionException(ConnectionFailureType.SocketFailure, testName);
                });
            }
            catch (Exception)
            {
                Assert.IsTrue(tries > RedisCache.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisCache.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(SocketException))]
        public void HandleRedis_ThrowSocketException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowSocketException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            try
            {
                cache.HandleRedis<bool>(testName, null, () =>
                {
                    tries++;
                    throw new SocketException();
                });
            }
            catch (Exception)
            {
                Assert.IsTrue(tries > RedisCache.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisCache.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        public async Task Get_NoSet_ReturnsDefaultValue()
        {
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"]);

            int cachedItem = await cache.Get<int>("Get_NoSet_ReturnsDefaultValue");

            Assert.AreEqual(cachedItem, default);
        }


        [TestMethod]
        public void NewRedisCache_NoConnectionString_IsConnectedFalse()
        {
            var cache = new RedisCache();
            Assert.IsFalse(cache.IsConnected);
        }

        [TestMethod]
        public void NewRedisCacheNoConnectionString_Connect_IsConnectedTrue()
        {
            var cache = new RedisCache();
            cache.Connect(_configuration["AzureCacheRedisConnectionString"]);
            Assert.IsTrue(cache.IsConnected);
        }

        [TestMethod]
        public void NewRedisCache_ConnectionString_IsConnectedTrue()
        {
            var cache = new RedisCache(_configuration["AzureCacheRedisConnectionString"]);
            Assert.IsTrue(cache.IsConnected);
        }
    }

    public class TestItem
    {
        public string Name { get; set; }

        public string Value { get; set; }

        public override int GetHashCode() => HashCode.Combine(Name, Value);

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            return obj.GetHashCode() == GetHashCode();
        }
    }
}