using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class TypingService : ITypingService
{
    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public TypingService(
        ITemporaryChatStore store,
        IChatEventPublisher events,
        IOptions<ChatRuntimeOptions> options,
        TimeProvider timeProvider)
    {
        _store = store;
        _events = events;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<TypingIndicator>> GetTypingIndicatorsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return _store.GetTypingIndicatorsAsync(ChatInput.NormalizeRoomCode(roomId), cancellationToken);
    }

    public async Task SetTypingAsync(SetTypingRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        var participant = await ChatGuards.GetRequiredParticipantAsync(_store, roomId, request.SessionId, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        if (request.IsTyping)
        {
            var indicator = new TypingIndicator(participant.SessionId, roomId, participant.Nickname, now);
            await _store.SetTypingAsync(indicator, _options.TypingTtl, cancellationToken);
        }
        else
        {
            await _store.RemoveTypingAsync(roomId, request.SessionId, cancellationToken);
        }

        var indicators = await _store.GetTypingIndicatorsAsync(roomId, cancellationToken);
        await _events.PublishAsync(new TypingUpdatedEvent(roomId, indicators, now), cancellationToken);
    }
}
