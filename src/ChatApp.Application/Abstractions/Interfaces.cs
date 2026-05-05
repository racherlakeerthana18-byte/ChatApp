using ChatApp.Application.Contracts;

namespace ChatApp.Application.Abstractions;

public interface ITemporaryChatStore
{
    Task<bool> TryCreateRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<Room?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Room>> GetRoomsAsync(CancellationToken cancellationToken = default);
    Task UpsertRoomAsync(Room room, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string roomId, DateTimeOffset minSentAtUtc, int maxCount, CancellationToken cancellationToken = default);
    Task AppendMessageAsync(ChatMessage message, DateTimeOffset minSentAtUtc, int maxCount, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveMessageAsync(string roomId, string messageId, DateTimeOffset removedAtUtc, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<bool> TryAddParticipantAsync(ParticipantSession participant, int maxParticipants, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<ParticipantSession?> GetParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParticipantSession>> GetParticipantsAsync(string roomId, CancellationToken cancellationToken = default);
    Task UpsertParticipantAsync(ParticipantSession participant, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TypingIndicator>> GetTypingIndicatorsAsync(string roomId, CancellationToken cancellationToken = default);
    Task SetTypingAsync(TypingIndicator indicator, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveTypingAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
    Task AddReportAsync(ReportRecord report, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportRecord>> GetReportsAsync(CancellationToken cancellationToken = default);
    Task AddModerationActionAsync(ModerationAction action, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModerationAction>> GetModerationActionsAsync(string? roomId = null, CancellationToken cancellationToken = default);
}

public interface IChatEventPublisher
{
    Task PublishAsync(ChatEvent chatEvent, CancellationToken cancellationToken = default);
}

public interface IBannedContentPolicy
{
    bool ContainsBlockedContent(string input);
}

public interface IActionThrottle
{
    Task EnforceLimitAsync(string scope, string key, int maxEvents, TimeSpan window, CancellationToken cancellationToken = default);
}

public interface IRoomService
{
    Task<Room> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<Room?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default);
    Task<RoomStateSnapshot?> GetRoomSnapshotAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ListedRoomSummary>> GetListedRoomsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ListedRoomSummary>> GetActiveRoomSummariesAsync(CancellationToken cancellationToken = default);
    Task<RoomStateSnapshot> JoinRoomAsync(JoinRoomRequest request, CancellationToken cancellationToken = default);
    Task LeaveRoomAsync(LeaveRoomRequest request, CancellationToken cancellationToken = default);
}

public interface IMessageService
{
    Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(string roomId, CancellationToken cancellationToken = default);
    Task<ChatMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
}

public interface IPresenceService
{
    Task<IReadOnlyList<ParticipantSession>> GetParticipantsAsync(string roomId, CancellationToken cancellationToken = default);
    Task<ParticipantSession?> GetParticipantAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
    Task HeartbeatAsync(string roomId, string sessionId, CancellationToken cancellationToken = default);
}

public interface ITypingService
{
    Task<IReadOnlyList<TypingIndicator>> GetTypingIndicatorsAsync(string roomId, CancellationToken cancellationToken = default);
    Task SetTypingAsync(SetTypingRequest request, CancellationToken cancellationToken = default);
}

public interface IReportingService
{
    Task<IReadOnlyList<ReportRecord>> GetReportsAsync(CancellationToken cancellationToken = default);
    Task<ReportRecord> ReportMessageAsync(ReportMessageRequest request, CancellationToken cancellationToken = default);
}

public interface IModerationService
{
    Task<IReadOnlyList<ModerationAction>> GetModerationHistoryAsync(string? roomId = null, CancellationToken cancellationToken = default);
    Task<ModerationAction> MuteParticipantAsync(MuteParticipantRequest request, CancellationToken cancellationToken = default);
    Task<ModerationAction> RemoveMessageAsync(RemoveMessageRequest request, CancellationToken cancellationToken = default);
    Task<ModerationAction> CloseRoomAsync(CloseRoomRequest request, CancellationToken cancellationToken = default);
}
