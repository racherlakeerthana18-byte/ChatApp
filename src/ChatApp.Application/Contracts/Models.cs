namespace ChatApp.Application.Contracts;

public enum RoomVisibility
{
    Listed = 0,
    Private = 1
}

public enum ModerationActionType
{
    MuteParticipant = 0,
    RemoveMessage = 1,
    CloseRoom = 2
}

public sealed record Room(
    string Id,
    string Name,
    RoomVisibility Visibility,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastActivityUtc,
    bool IsClosed = false,
    DateTimeOffset? ClosedAtUtc = null,
    string? ClosedReason = null);

public sealed record ParticipantSession(
    string SessionId,
    string RoomId,
    string Nickname,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    bool IsMuted = false);

public sealed record ChatMessage(
    string Id,
    string RoomId,
    string SessionId,
    string Nickname,
    string Content,
    DateTimeOffset SentAtUtc,
    bool IsRemoved = false,
    DateTimeOffset? RemovedAtUtc = null);

public sealed record TypingIndicator(
    string SessionId,
    string RoomId,
    string Nickname,
    DateTimeOffset StartedAtUtc);

public sealed record ListedRoomSummary(
    string Id,
    string Name,
    RoomVisibility Visibility,
    int ParticipantCount,
    DateTimeOffset LastActivityUtc,
    bool IsClosed = false);

public sealed record ReportRecord(
    string Id,
    string RoomId,
    string MessageId,
    string ReporterSessionId,
    string Reason,
    DateTimeOffset ReportedAtUtc);

public sealed record ModerationAction(
    string Id,
    string RoomId,
    ModerationActionType ActionType,
    string AdminId,
    DateTimeOffset OccurredAtUtc,
    string? TargetSessionId = null,
    string? TargetMessageId = null,
    string? Reason = null);

public sealed record RoomStateSnapshot(
    Room Room,
    ParticipantSession? CurrentParticipant,
    IReadOnlyList<ParticipantSession> Participants,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<TypingIndicator> TypingIndicators);

public sealed record CreateRoomRequest(
    string Name,
    RoomVisibility Visibility,
    string SessionId,
    string? IpAddress);

public sealed record JoinRoomRequest(
    string RoomId,
    string Nickname,
    string SessionId,
    string? IpAddress);

public sealed record LeaveRoomRequest(
    string RoomId,
    string SessionId);

public sealed record SendMessageRequest(
    string RoomId,
    string SessionId,
    string Content,
    string? IpAddress);

public sealed record SetTypingRequest(
    string RoomId,
    string SessionId,
    bool IsTyping);

public sealed record ReportMessageRequest(
    string RoomId,
    string MessageId,
    string SessionId,
    string Reason,
    string? IpAddress);

public sealed record MuteParticipantRequest(
    string RoomId,
    string SessionId,
    string AdminId,
    string Reason);

public sealed record RemoveMessageRequest(
    string RoomId,
    string MessageId,
    string AdminId,
    string Reason);

public sealed record CloseRoomRequest(
    string RoomId,
    string AdminId,
    string Reason);
