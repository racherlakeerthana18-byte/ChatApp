namespace ChatApp.Application.Contracts;

public abstract record ChatEvent(string EventName, DateTimeOffset OccurredAtUtc);

public sealed record RoomCatalogueChangedEvent(Room Room, int ParticipantCount, DateTimeOffset OccurredAtUtc)
    : ChatEvent(nameof(RoomCatalogueChangedEvent), OccurredAtUtc);

public sealed record MessageReceivedEvent(ChatMessage Message)
    : ChatEvent(nameof(MessageReceivedEvent), Message.SentAtUtc);

public sealed record MessageRemovedEvent(string RoomId, string MessageId, DateTimeOffset RemovedAtUtc)
    : ChatEvent(nameof(MessageRemovedEvent), RemovedAtUtc);

public sealed record PresenceUpdatedEvent(string RoomId, IReadOnlyList<ParticipantSession> Participants, DateTimeOffset OccurredAtUtc)
    : ChatEvent(nameof(PresenceUpdatedEvent), OccurredAtUtc);

public sealed record TypingUpdatedEvent(string RoomId, IReadOnlyList<TypingIndicator> Indicators, DateTimeOffset OccurredAtUtc)
    : ChatEvent(nameof(TypingUpdatedEvent), OccurredAtUtc);

public sealed record RoomClosedEvent(string RoomId, string Reason, DateTimeOffset ClosedAtUtc)
    : ChatEvent(nameof(RoomClosedEvent), ClosedAtUtc);

public sealed record ReportAcceptedEvent(ReportRecord Report)
    : ChatEvent(nameof(ReportAcceptedEvent), Report.ReportedAtUtc);

public sealed record ModerationUpdatedEvent(ModerationAction Action)
    : ChatEvent(nameof(ModerationUpdatedEvent), Action.OccurredAtUtc);
