using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class ModerationService : IModerationService
{
    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public ModerationService(
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

    public Task<IReadOnlyList<ModerationAction>> GetModerationHistoryAsync(string? roomId = null, CancellationToken cancellationToken = default)
    {
        return _store.GetModerationActionsAsync(roomId, cancellationToken);
    }

    public async Task<ModerationAction> MuteParticipantAsync(MuteParticipantRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        _ = await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken);
        var participant = await ChatGuards.GetRequiredParticipantAsync(_store, roomId, request.SessionId, cancellationToken);

        var updatedParticipant = participant with { IsMuted = true };
        await _store.UpsertParticipantAsync(updatedParticipant, _options.PresenceTtl, cancellationToken);

        var action = CreateAction(
            roomId,
            request.AdminId,
            ModerationActionType.MuteParticipant,
            request.Reason,
            targetSessionId: participant.SessionId);

        await RecordModerationActionAsync(action, cancellationToken);

        var participants = await _store.GetParticipantsAsync(roomId, cancellationToken);
        await _events.PublishAsync(new PresenceUpdatedEvent(roomId, participants, action.OccurredAtUtc), cancellationToken);

        return action;
    }

    public async Task<ModerationAction> RemoveMessageAsync(RemoveMessageRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        _ = await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken);

        await _store.RemoveMessageAsync(roomId, request.MessageId, _timeProvider.GetUtcNow(), _options.RoomTtl, cancellationToken);

        var action = CreateAction(
            roomId,
            request.AdminId,
            ModerationActionType.RemoveMessage,
            request.Reason,
            targetMessageId: request.MessageId);

        await RecordModerationActionAsync(action, cancellationToken);
        await _events.PublishAsync(new MessageRemovedEvent(roomId, request.MessageId, action.OccurredAtUtc), cancellationToken);

        return action;
    }

    public async Task<ModerationAction> CloseRoomAsync(CloseRoomRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        var room = await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var updatedRoom = room with
        {
            IsClosed = true,
            ClosedAtUtc = now,
            ClosedReason = ChatInput.NormalizeReason(request.Reason, "Closed by a moderator."),
            LastActivityUtc = now
        };

        await _store.UpsertRoomAsync(updatedRoom, _options.RoomTtl, cancellationToken);

        var action = CreateAction(
            roomId,
            request.AdminId,
            ModerationActionType.CloseRoom,
            updatedRoom.ClosedReason ?? "Closed by a moderator.");

        await RecordModerationActionAsync(action, cancellationToken);
        await _events.PublishAsync(new RoomClosedEvent(roomId, updatedRoom.ClosedReason ?? "Closed by a moderator.", now), cancellationToken);
        await _events.PublishAsync(new RoomCatalogueChangedEvent(updatedRoom, 0, now), cancellationToken);

        return action;
    }

    private ModerationAction CreateAction(
        string roomId,
        string adminId,
        ModerationActionType actionType,
        string reason,
        string? targetSessionId = null,
        string? targetMessageId = null)
    {
        return new ModerationAction(
            Guid.NewGuid().ToString("N"),
            roomId,
            actionType,
            adminId,
            _timeProvider.GetUtcNow(),
            targetSessionId,
            targetMessageId,
            ChatInput.NormalizeReason(reason, "No reason supplied."));
    }

    private async Task RecordModerationActionAsync(ModerationAction action, CancellationToken cancellationToken)
    {
        await _store.AddModerationActionAsync(action, _options.RoomTtl, cancellationToken);
        await _events.PublishAsync(new ModerationUpdatedEvent(action), cancellationToken);
    }
}
