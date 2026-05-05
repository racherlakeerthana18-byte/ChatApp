using System.Text.Json;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Infrastructure.Diagnostics;
using ChatApp.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ChatApp.Infrastructure.Storage;

public sealed class RedisTemporaryChatStore : ITemporaryChatStore, ITemporaryStoreDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly TemporaryStoreOptions _options;

    public RedisTemporaryChatStore(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<TemporaryStoreOptions> options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options.Value;
    }

    public async Task<bool> TryCreateRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        var roomStored = await database.StringSetAsync(RoomKey(room.Id), Serialize(room), ttl, When.NotExists);
        if (roomStored)
        {
            await database.SetAddAsync(RoomIndexKey(), room.Id);
        }

        return roomStored;
    }

    public async Task<Room?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var value = await GetDatabase().StringGetAsync(RoomKey(roomId));
        return Deserialize<Room>(value);
    }

    public async Task<IReadOnlyList<Room>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        var roomIds = await GetDatabase().SetMembersAsync(RoomIndexKey());
        if (roomIds.Length == 0)
        {
            return [];
        }

        var rooms = await StringGetManyAsync<Room>(roomIds.Select(id => RoomKey(id!)).ToArray());
        var staleIds = roomIds
            .Zip(rooms, (roomId, room) => new { RoomId = roomId, Room = room })
            .Where(item => item.Room is null)
            .Select(item => item.RoomId)
            .ToArray();

        if (staleIds.Length > 0)
        {
            await GetDatabase().SetRemoveAsync(RoomIndexKey(), staleIds);
        }

        return rooms
            .OfType<Room>()
            .OrderByDescending(room => room.LastActivityUtc)
            .ToList();
    }

    public async Task UpsertRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.StringSetAsync(RoomKey(room.Id), Serialize(room), ttl);
        await database.SetAddAsync(RoomIndexKey(), room.Id);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string roomId, DateTimeOffset minSentAtUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var messages = await ReadListAsync<ChatMessage>(MessagesKey(roomId));
        var recentMessages = messages
            .Where(message => message.SentAtUtc >= minSentAtUtc)
            .OrderBy(message => message.SentAtUtc)
            .TakeLast(maxCount)
            .ToList();

        if (recentMessages.Count != messages.Count)
        {
            await RewriteListAsync(MessagesKey(roomId), recentMessages, await GetRoomTtlAsync(roomId));
        }

        return recentMessages;
    }

    public async Task AppendMessageAsync(ChatMessage message, DateTimeOffset minSentAtUtc, int maxCount, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var messages = await ReadListAsync<ChatMessage>(MessagesKey(message.RoomId));
        messages = messages
            .Where(existing => existing.SentAtUtc >= minSentAtUtc)
            .OrderBy(existing => existing.SentAtUtc)
            .TakeLast(maxCount)
            .ToList();

        messages.Add(message);
        messages = messages
            .OrderBy(existing => existing.SentAtUtc)
            .TakeLast(maxCount)
            .ToList();

        await RewriteListAsync(MessagesKey(message.RoomId), messages, ttl);
    }

    public async Task RemoveMessageAsync(string roomId, string messageId, DateTimeOffset removedAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var messages = await ReadListAsync<ChatMessage>(MessagesKey(roomId));
        var updatedMessages = messages
            .Select(message => message.Id == messageId
                ? message with { IsRemoved = true, Content = string.Empty, RemovedAtUtc = removedAtUtc }
                : message)
            .ToList();

        await RewriteListAsync(MessagesKey(roomId), updatedMessages, ttl);
    }

    public async Task<bool> TryAddParticipantAsync(ParticipantSession participant, int maxParticipants, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var participants = await GetParticipantsAsync(participant.RoomId, cancellationToken);
        if (participants.All(existing => existing.SessionId != participant.SessionId) && participants.Count >= maxParticipants)
        {
            return false;
        }

        await UpsertParticipantAsync(participant, ttl, cancellationToken);
        return true;
    }

    public async Task<ParticipantSession?> GetParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        var value = await GetDatabase().StringGetAsync(ParticipantKey(roomId, sessionId));
        return Deserialize<ParticipantSession>(value);
    }

    public async Task<IReadOnlyList<ParticipantSession>> GetParticipantsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var participantIds = await GetDatabase().SetMembersAsync(ParticipantsIndexKey(roomId));
        if (participantIds.Length == 0)
        {
            return [];
        }

        var participants = await StringGetManyAsync<ParticipantSession>(
            participantIds.Select(id => ParticipantKey(roomId, id!)).ToArray());

        var staleIds = participantIds
            .Zip(participants, (participantId, participant) => new { ParticipantId = participantId, Participant = participant })
            .Where(item => item.Participant is null)
            .Select(item => item.ParticipantId)
            .ToArray();

        if (staleIds.Length > 0)
        {
            await GetDatabase().SetRemoveAsync(ParticipantsIndexKey(roomId), staleIds);
        }

        return participants
            .OfType<ParticipantSession>()
            .OrderBy(participant => participant.JoinedAtUtc)
            .ToList();
    }

    public async Task UpsertParticipantAsync(ParticipantSession participant, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.StringSetAsync(ParticipantKey(participant.RoomId, participant.SessionId), Serialize(participant), ttl);
        await database.SetAddAsync(ParticipantsIndexKey(participant.RoomId), participant.SessionId);
    }

    public async Task RemoveParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.KeyDeleteAsync(ParticipantKey(roomId, sessionId));
        await database.SetRemoveAsync(ParticipantsIndexKey(roomId), sessionId);
    }

    public async Task<IReadOnlyList<TypingIndicator>> GetTypingIndicatorsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var typingIds = await GetDatabase().SetMembersAsync(TypingIndexKey(roomId));
        if (typingIds.Length == 0)
        {
            return [];
        }

        var indicators = await StringGetManyAsync<TypingIndicator>(
            typingIds.Select(id => TypingKey(roomId, id!)).ToArray());

        var staleIds = typingIds
            .Zip(indicators, (typingId, indicator) => new { TypingId = typingId, Indicator = indicator })
            .Where(item => item.Indicator is null)
            .Select(item => item.TypingId)
            .ToArray();

        if (staleIds.Length > 0)
        {
            await GetDatabase().SetRemoveAsync(TypingIndexKey(roomId), staleIds);
        }

        return indicators
            .OfType<TypingIndicator>()
            .OrderBy(indicator => indicator.StartedAtUtc)
            .ToList();
    }

    public async Task SetTypingAsync(TypingIndicator indicator, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.StringSetAsync(TypingKey(indicator.RoomId, indicator.SessionId), Serialize(indicator), ttl);
        await database.SetAddAsync(TypingIndexKey(indicator.RoomId), indicator.SessionId);
    }

    public async Task RemoveTypingAsync(string roomId, string sessionId, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.KeyDeleteAsync(TypingKey(roomId, sessionId));
        await database.SetRemoveAsync(TypingIndexKey(roomId), sessionId);
    }

    public async Task AddReportAsync(ReportRecord report, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.StringSetAsync(ReportKey(report.Id), Serialize(report), ttl);
        await database.SetAddAsync(ReportsIndexKey(), report.Id);
    }

    public async Task<IReadOnlyList<ReportRecord>> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        var reportIds = await GetDatabase().SetMembersAsync(ReportsIndexKey());
        if (reportIds.Length == 0)
        {
            return [];
        }

        var reports = await StringGetManyAsync<ReportRecord>(reportIds.Select(id => ReportKey(id!)).ToArray());
        await RemoveStaleIndexEntriesAsync(ReportsIndexKey(), reportIds, reports);

        return reports
            .OfType<ReportRecord>()
            .OrderByDescending(report => report.ReportedAtUtc)
            .ToList();
    }

    public async Task AddModerationActionAsync(ModerationAction action, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var database = GetDatabase();
        await database.StringSetAsync(ModerationActionKey(action.Id), Serialize(action), ttl);
        await database.SetAddAsync(ModerationActionsIndexKey(), action.Id);
    }

    public async Task<IReadOnlyList<ModerationAction>> GetModerationActionsAsync(string? roomId = null, CancellationToken cancellationToken = default)
    {
        var actionIds = await GetDatabase().SetMembersAsync(ModerationActionsIndexKey());
        if (actionIds.Length == 0)
        {
            return [];
        }

        var actions = await StringGetManyAsync<ModerationAction>(actionIds.Select(id => ModerationActionKey(id!)).ToArray());
        await RemoveStaleIndexEntriesAsync(ModerationActionsIndexKey(), actionIds, actions);

        return actions
            .OfType<ModerationAction>()
            .Where(action => string.IsNullOrWhiteSpace(roomId) || string.Equals(action.RoomId, roomId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(action => action.OccurredAtUtc)
            .ToList();
    }

    public async Task<(bool IsHealthy, string Description)> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        await GetDatabase().PingAsync();
        return (true, "Redis temporary storage is reachable.");
    }

    private async Task RemoveStaleIndexEntriesAsync<T>(
        RedisKey indexKey,
        IReadOnlyList<RedisValue> ids,
        IReadOnlyList<T?> entries)
        where T : class
    {
        var staleIds = ids
            .Zip(entries, (id, entry) => new { Id = id, Entry = entry })
            .Where(item => item.Entry is null)
            .Select(item => item.Id)
            .ToArray();

        if (staleIds.Length > 0)
        {
            await GetDatabase().SetRemoveAsync(indexKey, staleIds);
        }
    }

    private async Task<List<T>> ReadListAsync<T>(RedisKey listKey)
    {
        var values = await GetDatabase().ListRangeAsync(listKey);
        return values
            .Select(Deserialize<T>)
            .OfType<T>()
            .ToList();
    }

    private async Task RewriteListAsync<T>(RedisKey listKey, IReadOnlyList<T> items, TimeSpan ttl)
    {
        var database = GetDatabase();
        await database.KeyDeleteAsync(listKey);

        if (items.Count > 0)
        {
            var values = items.Select(Serialize).ToArray();
            await database.ListRightPushAsync(listKey, values);
            await database.KeyExpireAsync(listKey, ttl);
        }
    }

    private async Task<TimeSpan> GetRoomTtlAsync(string roomId)
    {
        var ttl = await GetDatabase().KeyTimeToLiveAsync(RoomKey(roomId));
        return ttl ?? TimeSpan.FromHours(24);
    }

    private async Task<IReadOnlyList<T?>> StringGetManyAsync<T>(RedisKey[] keys)
    {
        if (keys.Length == 0)
        {
            return [];
        }

        var values = await GetDatabase().StringGetAsync(keys);
        return values.Select(Deserialize<T>).ToList();
    }

    private IDatabase GetDatabase() => _connectionMultiplexer.GetDatabase();

    private static RedisValue Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static T? Deserialize<T>(RedisValue value)
    {
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }

    private RedisKey RoomIndexKey() => $"{_options.KeyPrefix}:rooms";
    private RedisKey RoomKey(string roomId) => $"{_options.KeyPrefix}:room:{roomId}";
    private RedisKey MessagesKey(string roomId) => $"{_options.KeyPrefix}:room:{roomId}:messages";
    private RedisKey ParticipantsIndexKey(string roomId) => $"{_options.KeyPrefix}:room:{roomId}:participants";
    private RedisKey ParticipantKey(string roomId, string sessionId) => $"{_options.KeyPrefix}:room:{roomId}:participant:{sessionId}";
    private RedisKey TypingIndexKey(string roomId) => $"{_options.KeyPrefix}:room:{roomId}:typing";
    private RedisKey TypingKey(string roomId, string sessionId) => $"{_options.KeyPrefix}:room:{roomId}:typing:{sessionId}";
    private RedisKey ReportsIndexKey() => $"{_options.KeyPrefix}:reports";
    private RedisKey ReportKey(string reportId) => $"{_options.KeyPrefix}:report:{reportId}";
    private RedisKey ModerationActionsIndexKey() => $"{_options.KeyPrefix}:moderation";
    private RedisKey ModerationActionKey(string actionId) => $"{_options.KeyPrefix}:moderation:{actionId}";
}
