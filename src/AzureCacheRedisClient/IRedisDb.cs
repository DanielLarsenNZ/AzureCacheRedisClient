namespace AzureCacheRedisClient
{
    internal interface IRedisDb : IRedisCache
    {
        Task<long> Increment(string key, long increment = 1, bool fireAndForget = false);
    }
}