using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Exceptions;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class MessageService : IMessageService
{
    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly IBannedContentPolicy _bannedContentPolicy;
    private readonly IActionThrottle _throttle;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public MessageService(
        ITemporaryChatStore store,
        IChatEventPublisher events,
        IBannedContentPolicy bannedContentPolicy,
        IActionThrottle throttle,
        IOptions<ChatRuntimeOptions> options,
        TimeProvider timeProvider)
    {
        _store = store;
        _events = events;
        _bannedContentPolicy = bannedContentPolicy;
        _throttle = throttle;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return _store.GetMessagesAsync(
            ChatInput.NormalizeRoomCode(roomId),
            GetUtcNow().Subtract(_options.MessageTtl),
            _options.MaxMessageHistory,
            cancellationToken);
    }

    public async Task<ChatMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        var room = ChatGuards.EnsureRoomIsOpen(await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken));
        var participant = await ChatGuards.GetRequiredParticipantAsync(_store, roomId, request.SessionId, cancellationToken);
        var messageText = ChatInput.NormalizeMessage(request.Content);
        ChatInput.ValidateMessage(messageText, _options);

        if (participant.IsMuted)
        {
            throw new ChatDomainException("A moderator has muted you in this room.", "participant_muted");
        }

        if (_bannedContentPolicy.ContainsBlockedContent(messageText))
        {
            throw new ChatDomainException("That message contains blocked content.", "blocked_message");
        }

        await EnforceMessageThrottleAsync(request, cancellationToken);

        var now = GetUtcNow();
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            roomId,
            participant.SessionId,
            participant.Nickname,
            messageText,
            now);

        await _store.AppendMessageAsync(
            message,
            now.Subtract(_options.MessageTtl),
            _options.MaxMessageHistory,
            _options.RoomTtl,
            cancellationToken);

        await _store.RemoveTypingAsync(roomId, request.SessionId, cancellationToken);
        await _store.UpsertRoomAsync(room with { LastActivityUtc = now }, _options.RoomTtl, cancellationToken);

        var typingIndicators = await _store.GetTypingIndicatorsAsync(roomId, cancellationToken);
        await _events.PublishAsync(new MessageReceivedEvent(message), cancellationToken);
        await _events.PublishAsync(new TypingUpdatedEvent(roomId, typingIndicators, now), cancellationToken);

        return message;
    }

    private async Task EnforceMessageThrottleAsync(SendMessageRequest request, CancellationToken cancellationToken)
    {
        await _throttle.EnforceLimitAsync(
            "message-session",
            request.SessionId,
            _options.MessageLimitPerTenSeconds,
            _options.MessageWindow,
            cancellationToken);

        await _throttle.EnforceLimitAsync(
            "message-ip",
            ChatInput.SafeKey(request.IpAddress),
            _options.MessageLimitPerTenSeconds,
            _options.MessageWindow,
            cancellationToken);
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();
}
