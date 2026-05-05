using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Exceptions;

namespace ChatApp.Application.Internal;

internal static class ChatGuards
{
    public static async Task<Room> GetRequiredRoomAsync(
        ITemporaryChatStore store,
        string roomId,
        CancellationToken cancellationToken)
    {
        var room = await store.GetRoomAsync(roomId, cancellationToken);
        if (room is null)
        {
            throw new ChatDomainException("That room does not exist anymore.", "room_not_found");
        }

        return room;
    }

    public static Room EnsureRoomIsOpen(Room room)
    {
        if (room.IsClosed)
        {
            throw new ChatDomainException("This room has been closed.", "room_closed");
        }

        return room;
    }

    public static async Task<ParticipantSession> GetRequiredParticipantAsync(
        ITemporaryChatStore store,
        string roomId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var participant = await store.GetParticipantAsync(roomId, sessionId, cancellationToken);
        if (participant is null)
        {
            throw new ChatDomainException("Join the room before using chat actions.", "participant_not_found");
        }

        return participant;
    }
}
