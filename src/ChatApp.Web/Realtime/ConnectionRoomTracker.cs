namespace ChatApp.Web.Realtime;

public sealed class ConnectionRoomTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<string>> _roomsByConnection = [];

    public void Track(string connectionId, string roomId)
    {
        lock (_lock)
        {
            if (!_roomsByConnection.TryGetValue(connectionId, out var rooms))
            {
                rooms = [];
                _roomsByConnection[connectionId] = rooms;
            }

            rooms.Add(roomId);
        }
    }

    public void Untrack(string connectionId, string roomId)
    {
        lock (_lock)
        {
            if (!_roomsByConnection.TryGetValue(connectionId, out var rooms))
            {
                return;
            }

            rooms.Remove(roomId);
            if (rooms.Count == 0)
            {
                _roomsByConnection.Remove(connectionId);
            }
        }
    }

    public IReadOnlyList<string> RemoveConnection(string connectionId)
    {
        lock (_lock)
        {
            if (!_roomsByConnection.TryGetValue(connectionId, out var rooms))
            {
                return [];
            }

            _roomsByConnection.Remove(connectionId);
            return rooms.ToList();
        }
    }
}
