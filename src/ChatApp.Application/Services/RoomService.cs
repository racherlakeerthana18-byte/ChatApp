using System.Security.Cryptography;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Exceptions;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class RoomService : IRoomService
{
    private const string RoomCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly IBannedContentPolicy _bannedContentPolicy;
    private readonly IActionThrottle _throttle;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public RoomService(
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

    public async Task<Room> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedName = ChatInput.NormalizeRoomName(request.Name);
        ChatInput.ValidateRoomName(normalizedName, _options);

        if (_bannedContentPolicy.ContainsBlockedContent(normalizedName))
        {
            throw new ChatDomainException("That room name is not allowed.", "blocked_room_name");
        }

        var now = GetUtcNow();
        await EnforceRoomCreationThrottleAsync(request, cancellationToken);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var room = new Room(
                GenerateRoomCode(_options.RoomCodeLength),
                normalizedName,
                request.Visibility,
                now,
                now);

            if (await _store.TryCreateRoomAsync(room, _options.RoomTtl, cancellationToken))
            {
                await PublishRoomCatalogueChangedAsync(room, 0, now, cancellationToken);
                return room;
            }
        }

        throw new ChatDomainException("Could not create a unique room code. Please try again.", "room_code_collision");
    }

    public Task<Room?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return _store.GetRoomAsync(ChatInput.NormalizeRoomCode(roomId), cancellationToken);
    }

    public async Task<RoomStateSnapshot?> GetRoomSnapshotAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        var normalizedRoomId = ChatInput.NormalizeRoomCode(roomId);
        var room = await _store.GetRoomAsync(normalizedRoomId, cancellationToken);
        if (room is null)
        {
            return null;
        }

        var participants = await _store.GetParticipantsAsync(normalizedRoomId, cancellationToken);
        var messages = await _store.GetMessagesAsync(
            normalizedRoomId,
            GetUtcNow().Subtract(_options.MessageTtl),
            _options.MaxMessageHistory,
            cancellationToken);
        var typing = await _store.GetTypingIndicatorsAsync(normalizedRoomId, cancellationToken);
        var currentParticipant = participants.FirstOrDefault(participant => participant.SessionId == sessionId);

        return new RoomStateSnapshot(room, currentParticipant, participants, messages, typing);
    }

    public async Task<IReadOnlyList<ListedRoomSummary>> GetListedRoomsAsync(CancellationToken cancellationToken = default)
    {
        var allRooms = await GetRoomSummariesAsync(includePrivate: false, cancellationToken);
        return allRooms
            .Where(room => room.Visibility == RoomVisibility.Listed && !room.IsClosed)
            .Take(_options.ListedRoomLimit)
            .ToList();
    }

    public Task<IReadOnlyList<ListedRoomSummary>> GetActiveRoomSummariesAsync(CancellationToken cancellationToken = default)
    {
        return GetRoomSummariesAsync(includePrivate: true, cancellationToken);
    }

    public async Task<RoomStateSnapshot> JoinRoomAsync(JoinRoomRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        var nickname = ChatInput.NormalizeNickname(request.Nickname);
        ChatInput.ValidateNickname(nickname, _options);

        if (_bannedContentPolicy.ContainsBlockedContent(nickname))
        {
            throw new ChatDomainException("That nickname is not allowed.", "blocked_nickname");
        }

        var room = ChatGuards.EnsureRoomIsOpen(await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken));
        var now = GetUtcNow();

        var existingParticipant = await _store.GetParticipantAsync(roomId, request.SessionId, cancellationToken);
        ParticipantSession participant;

        if (existingParticipant is null)
        {
            participant = new ParticipantSession(request.SessionId, roomId, nickname, now, now);
            var participantAdded = await _store.TryAddParticipantAsync(
                participant,
                _options.MaxRoomParticipants,
                _options.PresenceTtl,
                cancellationToken);

            if (!participantAdded)
            {
                throw new ChatDomainException("That room is full right now.", "room_full");
            }
        }
        else
        {
            participant = existingParticipant with
            {
                Nickname = nickname,
                LastSeenAtUtc = now
            };

            await _store.UpsertParticipantAsync(participant, _options.PresenceTtl, cancellationToken);
        }

        await _store.RemoveTypingAsync(roomId, request.SessionId, cancellationToken);
        room = await TouchRoomAsync(room, now, cancellationToken);

        var participants = await _store.GetParticipantsAsync(roomId, cancellationToken);
        var messages = await _store.GetMessagesAsync(roomId, now.Subtract(_options.MessageTtl), _options.MaxMessageHistory, cancellationToken);
        var typing = await _store.GetTypingIndicatorsAsync(roomId, cancellationToken);

        await _events.PublishAsync(new PresenceUpdatedEvent(roomId, participants, now), cancellationToken);
        await PublishRoomCatalogueChangedAsync(room, participants.Count, now, cancellationToken);

        return new RoomStateSnapshot(room, participant, participants, messages, typing);
    }

    public async Task LeaveRoomAsync(LeaveRoomRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        await _store.RemoveTypingAsync(roomId, request.SessionId, cancellationToken);
        await _store.RemoveParticipantAsync(roomId, request.SessionId, cancellationToken);

        var room = await _store.GetRoomAsync(roomId, cancellationToken);
        if (room is null)
        {
            return;
        }

        var participants = await _store.GetParticipantsAsync(roomId, cancellationToken);
        var now = GetUtcNow();

        await _events.PublishAsync(new PresenceUpdatedEvent(roomId, participants, now), cancellationToken);
        await PublishRoomCatalogueChangedAsync(room, participants.Count, now, cancellationToken);
    }

    private async Task<IReadOnlyList<ListedRoomSummary>> GetRoomSummariesAsync(bool includePrivate, CancellationToken cancellationToken)
    {
        var rooms = await _store.GetRoomsAsync(cancellationToken);
        var summaries = new List<ListedRoomSummary>(rooms.Count);

        foreach (var room in rooms)
        {
            if (!includePrivate && room.Visibility == RoomVisibility.Private)
            {
                continue;
            }

            var participants = await _store.GetParticipantsAsync(room.Id, cancellationToken);
            summaries.Add(new ListedRoomSummary(
                room.Id,
                room.Name,
                room.Visibility,
                participants.Count,
                room.LastActivityUtc,
                room.IsClosed));
        }

        return summaries
            .OrderByDescending(room => room.ParticipantCount)
            .ThenByDescending(room => room.LastActivityUtc)
            .ToList();
    }

    private async Task EnforceRoomCreationThrottleAsync(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        await _throttle.EnforceLimitAsync(
            "room-create-session",
            request.SessionId,
            _options.CreateRoomLimitPerMinute,
            _options.CreateRoomWindow,
            cancellationToken);

        await _throttle.EnforceLimitAsync(
            "room-create-ip",
            ChatInput.SafeKey(request.IpAddress),
            _options.CreateRoomLimitPerMinute,
            _options.CreateRoomWindow,
            cancellationToken);
    }

    private async Task<Room> TouchRoomAsync(Room room, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var updatedRoom = room with { LastActivityUtc = now };
        await _store.UpsertRoomAsync(updatedRoom, _options.RoomTtl, cancellationToken);
        return updatedRoom;
    }

    private Task PublishRoomCatalogueChangedAsync(Room room, int participantCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        return _events.PublishAsync(new RoomCatalogueChangedEvent(room, participantCount, now), cancellationToken);
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();

    private static string GenerateRoomCode(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var characters = new char[length];

        for (var index = 0; index < length; index++)
        {
            characters[index] = RoomCodeAlphabet[bytes[index] % RoomCodeAlphabet.Length];
        }

        return new string(characters);
    }
}
