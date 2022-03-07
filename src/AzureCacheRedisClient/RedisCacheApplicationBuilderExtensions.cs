using AzureCacheRedisClient;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
    public static class RedisCacheApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseRedisCache(this IApplicationBuilder app, string? connectionString = null)
        {
            var redis = app.ApplicationServices.GetService<RedisCache>();

            if (redis is null)
            {
                throw new NullReferenceException($"A service named \"{typeof(RedisCache).Name}\" cannot be found in the Application Services. Ensure `services.AddSingleton<RedisCache>()` is called in ConfigureServices().");
            }

            var telemetry = app.ApplicationServices.GetService<TelemetryClient>();

            if (telemetry != null) redis.TelemetryClient = telemetry;

            if (!string.IsNullOrWhiteSpace(connectionString)) redis.Connect(connectionString);

            return app;
        }
    }
}