using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Realtime;

public sealed class ChatEventBroker : IChatEventPublisher, IChatEventFeed
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Func<ChatEvent, Task>> _subscriptions = [];
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    private readonly ILogger<ChatEventBroker> _logger;

    public ChatEventBroker(
        IHubContext<ChatHub, IChatClient> hubContext,
        ILogger<ChatEventBroker> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public IDisposable Subscribe(Func<ChatEvent, Task> handler)
    {
        var subscriptionId = Guid.NewGuid();

        lock (_lock)
        {
            _subscriptions[subscriptionId] = handler;
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                _subscriptions.Remove(subscriptionId);
            }
        });
    }

    public async Task PublishAsync(ChatEvent chatEvent, CancellationToken cancellationToken = default)
    {
        Func<ChatEvent, Task>[] handlers;

        lock (_lock)
        {
            handlers = _subscriptions.Values.ToArray();
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(chatEvent);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A chat event subscriber failed while processing {EventName}.", chatEvent.EventName);
            }
        }

        await BroadcastToHubAsync(chatEvent);
    }

    private Task BroadcastToHubAsync(ChatEvent chatEvent)
    {
        return chatEvent switch
        {
            MessageReceivedEvent messageReceived => _hubContext.Clients.Group(ChatHub.RoomGroup(messageReceived.Message.RoomId)).MessageReceived(messageReceived.Message),
            MessageRemovedEvent messageRemoved => _hubContext.Clients.Group(ChatHub.RoomGroup(messageRemoved.RoomId)).MessageRemoved(messageRemoved.MessageId),
            PresenceUpdatedEvent presenceUpdated => _hubContext.Clients.Group(ChatHub.RoomGroup(presenceUpdated.RoomId)).PresenceUpdated(presenceUpdated.Participants),
            TypingUpdatedEvent typingUpdated => _hubContext.Clients.Group(ChatHub.RoomGroup(typingUpdated.RoomId)).TypingUpdated(typingUpdated.Indicators),
            RoomClosedEvent roomClosed => _hubContext.Clients.Group(ChatHub.RoomGroup(roomClosed.RoomId)).RoomClosed(roomClosed.RoomId, roomClosed.Reason),
            _ => Task.CompletedTask
        };
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _unsubscribe();
            _disposed = true;
        }
    }
}
