using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChatApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddChatApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<ITypingService, TypingService>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<IModerationService, ModerationService>();

        services.TryAddSingleton<IChatEventPublisher, NullChatEventPublisher>();

        return services;
    }

    private sealed class NullChatEventPublisher : IChatEventPublisher
    {
        public Task PublishAsync(ChatEvent chatEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
