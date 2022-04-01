namespace AzureCacheRedisClient
{
    internal interface IRedisDb : IRedisCache
    {
        Task<long> Increment(string key, long increment = 1, bool fireAndForget = false);
        Task<long> Decrement(string key, long decrement = 1, bool fireAndForget = false);
    }
}