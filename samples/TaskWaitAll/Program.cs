using StackExchange.Redis;

Console.WriteLine("Hello, World!");

ConnectionMultiplexer.Connect(
                        "helloaspnet-eus2-redis.redis.cache.windows.net:6380,password=D6F429jOBaCodRZOvOpsfLcRmu7QKuk8WAzCaBgzM4w=,ssl=True,abortConnect=False",
                        configure: new Action<ConfigurationOptions>(options =>
                        {
                            options.AbortOnConnectFail = false;

                        }));

