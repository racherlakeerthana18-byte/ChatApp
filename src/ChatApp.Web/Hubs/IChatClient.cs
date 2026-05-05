using ChatApp.Application.Contracts;

namespace ChatApp.Web.Hubs;

public interface IChatClient
{
    Task RoomLoaded(RoomStateSnapshot snapshot);
    Task MessageReceived(ChatMessage message);
    Task MessageRemoved(string messageId);
    Task PresenceUpdated(IReadOnlyList<ParticipantSession> participants);
    Task TypingUpdated(IReadOnlyList<TypingIndicator> indicators);
    Task RoomClosed(string roomId, string reason);
    Task ReportAccepted(ReportRecord report);
}
