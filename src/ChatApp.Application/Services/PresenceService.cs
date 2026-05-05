using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class PresenceService : IPresenceService
{
    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public PresenceService(
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

    public Task<IReadOnlyList<ParticipantSession>> GetParticipantsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return _store.GetParticipantsAsync(ChatInput.NormalizeRoomCode(roomId), cancellationToken);
    }

    public Task<ParticipantSession?> GetParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        return _store.GetParticipantAsync(ChatInput.NormalizeRoomCode(roomId), sessionId, cancellationToken);
    }

    public async Task HeartbeatAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        var normalizedRoomId = ChatInput.NormalizeRoomCode(roomId);
        var participant = await _store.GetParticipantAsync(normalizedRoomId, sessionId, cancellationToken);
        if (participant is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var updatedParticipant = participant with { LastSeenAtUtc = now };
        await _store.UpsertParticipantAsync(updatedParticipant, _options.PresenceTtl, cancellationToken);

        var participants = await _store.GetParticipantsAsync(normalizedRoomId, cancellationToken);
        await _events.PublishAsync(new PresenceUpdatedEvent(normalizedRoomId, participants, now), cancellationToken);
    }
}
