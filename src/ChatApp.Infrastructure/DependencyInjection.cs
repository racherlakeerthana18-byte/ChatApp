using ChatApp.Application.Abstractions;
using ChatApp.Infrastructure.Abuse;
using ChatApp.Infrastructure.Diagnostics;
using ChatApp.Infrastructure.Moderation;
using ChatApp.Infrastructure.Options;
using ChatApp.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ChatApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddChatInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TemporaryStoreOptions>(configuration.GetSection(TemporaryStoreOptions.SectionName));
        services.AddSingleton<IActionThrottle, InMemoryActionThrottle>();
        services.AddSingleton<IBannedContentPolicy, ListBasedBannedContentPolicy>();

        var temporaryStoreOptions = configuration.GetSection(TemporaryStoreOptions.SectionName).Get<TemporaryStoreOptions>() ?? new TemporaryStoreOptions();
        var useRedis = string.Equals(temporaryStoreOptions.Provider, "Redis", StringComparison.OrdinalIgnoreCase);
        var redisConnectionString = configuration.GetConnectionString("Redis");

        if (useRedis && !string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<RedisTemporaryChatStore>();
            services.AddSingleton<ITemporaryChatStore>(provider => provider.GetRequiredService<RedisTemporaryChatStore>());
            services.AddSingleton<ITemporaryStoreDiagnostics>(provider => provider.GetRequiredService<RedisTemporaryChatStore>());
        }
        else
        {
            services.AddSingleton<InMemoryTemporaryChatStore>();
            services.AddSingleton<ITemporaryChatStore>(provider => provider.GetRequiredService<InMemoryTemporaryChatStore>());
            services.AddSingleton<ITemporaryStoreDiagnostics>(provider => provider.GetRequiredService<InMemoryTemporaryChatStore>());
        }

        return services;
    }
}
