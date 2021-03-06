using AzureCacheRedisClient;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AzureCacheRedisClientTests
{
    [TestClass]
    public class RedisDbTests
    {
        private readonly IConfiguration _configuration;
        private readonly TelemetryClient _telemetry;
        private readonly IOperationHolder<RequestTelemetry> _operation;

        public RedisDbTests()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(@"appsettings.json", false, false)
                .AddJsonFile(@"appsettings.Development.json", true, false)
                .Build();

            _telemetry = new TelemetryClient(TelemetryConfiguration.CreateDefault());
            if (!string.IsNullOrWhiteSpace(_configuration["APPINSIGHTS_INSTRUMENTATIONKEY"]))
            {
                _telemetry.InstrumentationKey = _configuration["APPINSIGHTS_INSTRUMENTATIONKEY"];
            }

            var request = new RequestTelemetry { Name = nameof(RedisDbTests) };
            request.Context.Operation.Id = Guid.NewGuid().ToString("N");
            request.Context.Operation.ParentId = Guid.NewGuid().ToString("N");

            _operation = _telemetry.StartOperation(request);
        }

        ~RedisDbTests()
        {
            _telemetry.StopOperation(_operation);
            _telemetry.Flush();
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task UsageTest()
        {
            IRedisCache cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            var item = new TestItem { Name = "foo", Value = "bar" };

            await cache.Set("foobar1", item, TimeSpan.FromSeconds(1));
            var cachedItem = await cache.Get<TestItem>("foobar1");

            Assert.AreEqual(item, cachedItem);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task TaskWhenAllUsageTest()
        {
            const string key = "foobar1";
            IRedisCache cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            var item = new TestItem { Name = "foo", Value = "bar" };

            var tasks = new List<Task>();
            

            await cache.Set(key, item, TimeSpan.FromSeconds(1));
            
            tasks.Add(cache.Get<TestItem>(key));
            tasks.Add(cache.Get<TestItem>(key));
            tasks.Add(cache.Get<TestItem>(key));

            // act
            await Task.WhenAll(tasks);

            await cache.Delete(key);
        }


        [TestMethod]
        [TestCategory("Integration")]
        public async Task DeleteKey_SuccessfullyRemovesKey()
        {
            IRedisCache cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            var item = new TestItem { Name= "foo", Value = "bar" };
            await cache.Set("foobar1", item, TimeSpan.FromSeconds(10));

            await cache.Delete("foobar1");

            var cachedItem = await cache.Get<TestItem>("foobar1");

            Assert.IsNull(cachedItem);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task DeleteKey_IgnoresKeyWhenItDoesNotExist()
        {
            IRedisCache cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"], _telemetry);

            var item = new TestItem { Name = "foo", Value = "bar" };
            await cache.Set("foobar1", item, TimeSpan.FromSeconds(10));

            await cache.Delete("foobar2");

            var cachedItem = await cache.Get<TestItem>("foobar1");
            var nonExistentKey = await cache.Get<TestItem>("foobar2");

            Assert.AreEqual(item, cachedItem);
            Assert.IsNull(nonExistentKey);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        [Timeout(1000)]
        public void HandleRedis_ExceptionThrown_ThrowException()
        {
            string testName = nameof(HandleRedis_ExceptionThrown_ThrowException);
            var cache = new RedisDb();

            cache.HandleRedis<bool>(testName, null, () =>
            {
                throw new Exception(testName);
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void HandleRedis_ThrowObjectDisposedException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowObjectDisposedException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisDb();
            
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
                Assert.IsTrue(tries > RedisDb.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisDb.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(RedisConnectionException))]
        public void HandleRedis_ThrowRedisConnectionException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowRedisConnectionException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisDb();

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
                Assert.IsTrue(tries > RedisDb.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisDb.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        [ExpectedException(typeof(SocketException))]
        public void HandleRedis_ThrowSocketException_TriesMoreThanRetryMaxAttempts()
        {
            string testName = nameof(HandleRedis_ThrowSocketException_TriesMoreThanRetryMaxAttempts);
            int tries = 0;
            var cache = new RedisDb();

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
                Assert.IsTrue(tries > RedisDb.RetryMaxAttempts, $"HandleRedis should retry at least RetryMaxAttempts ({RedisDb.RetryMaxAttempts}) times before throwing.");
                throw;
            }
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Get_NoSet_ReturnsDefaultValue()
        {
            IRedisCache cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            int cachedItem = await cache.Get<int>("Get_NoSet_ReturnsDefaultValue");

            Assert.AreEqual(default, cachedItem);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Increment_NoValueSet_Returns1()
        {
            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            string key = Guid.NewGuid().ToString("N");

            Assert.AreEqual(1, await cache.Increment(key));

            await cache.Delete(key);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Increment_NoValueSetIncrement5_Returns5()
        {
            const long number = 5;

            string key = Guid.NewGuid().ToString("N");

            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            Assert.AreEqual(number, await cache.Increment(key, number));

            await cache.Delete(key);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Increment_ValueSet_ReturnsPlus1()
        {
            const long number = 1;
            
            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            await cache.Set(nameof(Increment_ValueSet_ReturnsPlus1), number, TimeSpan.FromSeconds(1));

            Assert.AreEqual(number + 1, await cache.Increment(nameof(Increment_ValueSet_ReturnsPlus1)));

        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Increment_Increment5FireAndForget_Returns0()
        {
            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            Assert.AreEqual(default, await cache.Increment(nameof(Increment_ValueSet_ReturnsPlus1), 5, fireAndForget: true));

        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Decrement_NoValueSet_ReturnsMinus1()
        {
            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            string key = Guid.NewGuid().ToString("N");

            Assert.AreEqual(-1, await cache.Decrement(key));

            await cache.Delete(key);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Decrement_NoValueSetDecrement5_ReturnsMinus5()
        {
            const long number = 5;

            string key = Guid.NewGuid().ToString("N");

            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            Assert.AreEqual(-5, await cache.Decrement(key, number));

            await cache.Delete(key);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Decrement_ValueSet_ReturnMinus1()
        {
            const long number = 1;

            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            await cache.Set(nameof(Decrement_ValueSet_ReturnMinus1), number, TimeSpan.FromSeconds(1));

            Assert.AreEqual(number - 1, await cache.Decrement(nameof(Decrement_ValueSet_ReturnMinus1)));
        }

        [TestMethod]
        [TestCategory("Integration")]
        public async Task Decrement_Decrement5FireAndForget_Returns0()
        {
            IRedisDb cache = new RedisDb(_configuration["AzureCacheRedisConnectionString"]);

            Assert.AreEqual(default, await cache.Decrement(nameof(Decrement_ValueSet_ReturnMinus1), 5, fireAndForget: true));
        }

        [TestMethod]
        public void NewRedisDb_NoConnectionString_IsConnectedFalse()
        {
            var cache = new RedisDb();
            Assert.IsFalse(cache.IsConnected);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void NewRedisDbNoConnectionString_Connect_IsConnectedTrue()
        {
            var cache = new RedisDb();
            cache.Connect(_configuration["AzureCacheRedisConnectionString"]);
            Assert.IsTrue(cache.IsConnected);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void NewRedisDb_ConnectionString_IsConnectedTrue()
        {
            var cache = new RedisDb("FakeConnectionString");
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