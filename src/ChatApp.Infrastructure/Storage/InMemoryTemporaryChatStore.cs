using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Infrastructure.Diagnostics;

namespace ChatApp.Infrastructure.Storage;

public sealed class InMemoryTemporaryChatStore : ITemporaryChatStore, ITemporaryStoreDiagnostics
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, ExpiringValue<Room>> _rooms = [];
    private readonly Dictionary<string, List<ChatMessage>> _messagesByRoom = [];
    private readonly Dictionary<string, Dictionary<string, ExpiringValue<ParticipantSession>>> _participantsByRoom = [];
    private readonly Dictionary<string, Dictionary<string, ExpiringValue<TypingIndicator>>> _typingByRoom = [];
    private readonly Dictionary<string, ExpiringValue<ReportRecord>> _reports = [];
    private readonly Dictionary<string, ExpiringValue<ModerationAction>> _moderationActions = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryTemporaryChatStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<bool> TryCreateRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredState();

            if (_rooms.ContainsKey(room.Id))
            {
                return Task.FromResult(false);
            }

            _rooms[room.Id] = CreateExpiringValue(room, GetUtcNow().Add(ttl));
            return Task.FromResult(true);
        }
    }

    public Task<Room?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredState();
            return Task.FromResult(_rooms.TryGetValue(roomId, out var room) ? room.Value : null);
        }
    }

    public Task<IReadOnlyList<Room>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredState();
            return Task.FromResult<IReadOnlyList<Room>>(
                _rooms.Values
                    .Select(entry => entry.Value)
                    .OrderByDescending(room => room.LastActivityUtc)
                    .ToList());
        }
    }

    public Task UpsertRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rooms[room.Id] = CreateExpiringValue(room, GetUtcNow().Add(ttl));
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string roomId, DateTimeOffset minSentAtUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredState();
            var messages = GetRecentMessages(roomId, minSentAtUtc, maxCount);
            return Task.FromResult<IReadOnlyList<ChatMessage>>(messages);
        }
    }

    public Task AppendMessageAsync(ChatMessage message, DateTimeOffset minSentAtUtc, int maxCount, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var messages = GetRecentMessages(message.RoomId, minSentAtUtc, maxCount).ToList();
            messages.Add(message);
            _messagesByRoom[message.RoomId] = messages
                .OrderBy(chatMessage => chatMessage.SentAtUtc)
                .TakeLast(maxCount)
                .ToList();

            if (_rooms.TryGetValue(message.RoomId, out var room))
            {
                _rooms[message.RoomId] = CreateExpiringValue(room.Value with { LastActivityUtc = message.SentAtUtc }, GetUtcNow().Add(ttl));
            }

            return Task.CompletedTask;
        }
    }

    public Task RemoveMessageAsync(string roomId, string messageId, DateTimeOffset removedAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_messagesByRoom.TryGetValue(roomId, out var messages))
            {
                var updatedMessages = messages
                    .Select(message => message.Id == messageId
                        ? message with { IsRemoved = true, Content = string.Empty, RemovedAtUtc = removedAtUtc }
                        : message)
                    .ToList();

                _messagesByRoom[roomId] = updatedMessages;
            }

            if (_rooms.TryGetValue(roomId, out var room))
            {
                _rooms[roomId] = CreateExpiringValue(room.Value, GetUtcNow().Add(ttl));
            }

            return Task.CompletedTask;
        }
    }

    public Task<bool> TryAddParticipantAsync(ParticipantSession participant, int maxParticipants, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var participants = GetOrCreateParticipants(participant.RoomId);
            PruneExpiredEntries(participants);

            if (!participants.ContainsKey(participant.SessionId) && participants.Count >= maxParticipants)
            {
                return Task.FromResult(false);
            }

            participants[participant.SessionId] = CreateExpiringValue(participant, GetUtcNow().Add(ttl));
            return Task.FromResult(true);
        }
    }

    public Task<ParticipantSession?> GetParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var participants = GetOrCreateParticipants(roomId);
            PruneExpiredEntries(participants);

            return Task.FromResult(participants.TryGetValue(sessionId, out var participant) ? participant.Value : null);
        }
    }

    public Task<IReadOnlyList<ParticipantSession>> GetParticipantsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var participants = GetOrCreateParticipants(roomId);
            PruneExpiredEntries(participants);

            return Task.FromResult<IReadOnlyList<ParticipantSession>>(
                participants.Values
                    .Select(entry => entry.Value)
                    .OrderBy(participant => participant.JoinedAtUtc)
                    .ToList());
        }
    }

    public Task UpsertParticipantAsync(ParticipantSession participant, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var participants = GetOrCreateParticipants(participant.RoomId);
            participants[participant.SessionId] = CreateExpiringValue(participant, GetUtcNow().Add(ttl));
            return Task.CompletedTask;
        }
    }

    public Task RemoveParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_participantsByRoom.TryGetValue(roomId, out var participants))
            {
                participants.Remove(sessionId);
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TypingIndicator>> GetTypingIndicatorsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var typingEntries = GetOrCreateTyping(roomId);
            PruneExpiredEntries(typingEntries);

            return Task.FromResult<IReadOnlyList<TypingIndicator>>(
                typingEntries.Values
                    .Select(entry => entry.Value)
                    .OrderBy(indicator => indicator.StartedAtUtc)
                    .ToList());
        }
    }

    public Task SetTypingAsync(TypingIndicator indicator, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var typingEntries = GetOrCreateTyping(indicator.RoomId);
            typingEntries[indicator.SessionId] = CreateExpiringValue(indicator, GetUtcNow().Add(ttl));
            return Task.CompletedTask;
        }
    }

    public Task RemoveTypingAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_typingByRoom.TryGetValue(roomId, out var typingEntries))
            {
                typingEntries.Remove(sessionId);
            }

            return Task.CompletedTask;
        }
    }

    public Task AddReportAsync(ReportRecord report, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _reports[report.Id] = CreateExpiringValue(report, GetUtcNow().Add(ttl));
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<ReportRecord>> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredEntries(_reports);
            return Task.FromResult<IReadOnlyList<ReportRecord>>(
                _reports.Values
                    .Select(entry => entry.Value)
                    .OrderByDescending(report => report.ReportedAtUtc)
                    .ToList());
        }
    }

    public Task AddModerationActionAsync(ModerationAction action, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _moderationActions[action.Id] = CreateExpiringValue(action, GetUtcNow().Add(ttl));
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<ModerationAction>> GetModerationActionsAsync(string? roomId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            PruneExpiredEntries(_moderationActions);
            return Task.FromResult<IReadOnlyList<ModerationAction>>(
                _moderationActions.Values
                    .Select(entry => entry.Value)
                    .Where(action => string.IsNullOrWhiteSpace(roomId) || string.Equals(action.RoomId, roomId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(action => action.OccurredAtUtc)
                    .ToList());
        }
    }

    public Task<(bool IsHealthy, string Description)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((true, "Using in-memory temporary storage."));
    }

    private List<ChatMessage> GetRecentMessages(string roomId, DateTimeOffset minSentAtUtc, int maxCount)
    {
        if (!_messagesByRoom.TryGetValue(roomId, out var messages))
        {
            return [];
        }

        var recentMessages = messages
            .Where(message => message.SentAtUtc >= minSentAtUtc)
            .OrderBy(message => message.SentAtUtc)
            .TakeLast(maxCount)
            .ToList();

        _messagesByRoom[roomId] = recentMessages;
        return recentMessages;
    }

    private Dictionary<string, ExpiringValue<ParticipantSession>> GetOrCreateParticipants(string roomId)
    {
        if (_participantsByRoom.TryGetValue(roomId, out var participants))
        {
            return participants;
        }

        participants = [];
        _participantsByRoom[roomId] = participants;
        return participants;
    }

    private Dictionary<string, ExpiringValue<TypingIndicator>> GetOrCreateTyping(string roomId)
    {
        if (_typingByRoom.TryGetValue(roomId, out var typingEntries))
        {
            return typingEntries;
        }

        typingEntries = [];
        _typingByRoom[roomId] = typingEntries;
        return typingEntries;
    }

    private void PruneExpiredState()
    {
        var now = GetUtcNow();
        var expiredRoomIds = _rooms
            .Where(pair => pair.Value.ExpiresAtUtc <= now)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var roomId in expiredRoomIds)
        {
            _rooms.Remove(roomId);
            _messagesByRoom.Remove(roomId);
            _participantsByRoom.Remove(roomId);
            _typingByRoom.Remove(roomId);
        }

        foreach (var participants in _participantsByRoom.Values)
        {
            PruneExpiredEntries(participants);
        }

        foreach (var typingEntries in _typingByRoom.Values)
        {
            PruneExpiredEntries(typingEntries);
        }

        PruneExpiredEntries(_reports);
        PruneExpiredEntries(_moderationActions);
    }

    private void PruneExpiredEntries<TValue>(Dictionary<string, ExpiringValue<TValue>> entries)
    {
        var now = GetUtcNow();
        var expiredKeys = entries
            .Where(pair => pair.Value.ExpiresAtUtc <= now)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            entries.Remove(key);
        }
    }

    private DateTimeOffset GetUtcNow() => _timeProvider.GetUtcNow();

    private static ExpiringValue<T> CreateExpiringValue<T>(T value, DateTimeOffset expiresAtUtc) => new(value, expiresAtUtc);

    private sealed record ExpiringValue<T>(T Value, DateTimeOffset ExpiresAtUtc)
    {
        public static ExpiringValue<T> Create(T value, DateTimeOffset expiresAtUtc) => new(value, expiresAtUtc);
    }
}
