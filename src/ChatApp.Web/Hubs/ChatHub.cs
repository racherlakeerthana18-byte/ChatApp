using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Web.Realtime;
using ChatApp.Web.Session;
using Microsoft.AspNetCore.SignalR;

namespace ChatApp.Web.Hubs;

public sealed class ChatHub : Hub<IChatClient>
{
    private readonly IRoomService _roomService;
    private readonly IMessageService _messageService;
    private readonly ITypingService _typingService;
    private readonly IReportingService _reportingService;
    private readonly ConnectionRoomTracker _connectionRoomTracker;

    public ChatHub(
        IRoomService roomService,
        IMessageService messageService,
        ITypingService typingService,
        IReportingService reportingService,
        ConnectionRoomTracker connectionRoomTracker)
    {
        _roomService = roomService;
        _messageService = messageService;
        _typingService = typingService;
        _reportingService = reportingService;
        _connectionRoomTracker = connectionRoomTracker;
    }

    public static string RoomGroup(string roomId) => $"room:{roomId}";

    public async Task<RoomStateSnapshot> JoinRoom(string roomId, string nickname)
    {
        var request = new JoinRoomRequest(
            roomId,
            nickname,
            GetSessionId(),
            GetIpAddress());

        var snapshot = await _roomService.JoinRoomAsync(request);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(snapshot.Room.Id));
        _connectionRoomTracker.Track(Context.ConnectionId, snapshot.Room.Id);
        await Clients.Caller.RoomLoaded(snapshot);

        return snapshot;
    }

    public async Task LeaveRoom(string roomId)
    {
        await _roomService.LeaveRoomAsync(new LeaveRoomRequest(roomId, GetSessionId()));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomGroup(roomId));
        _connectionRoomTracker.Untrack(Context.ConnectionId, roomId);
    }

    public Task<ChatMessage> SendMessage(string roomId, string content)
    {
        return _messageService.SendMessageAsync(new SendMessageRequest(roomId, GetSessionId(), content, GetIpAddress()));
    }

    public Task SetTyping(string roomId, bool isTyping)
    {
        return _typingService.SetTypingAsync(new SetTypingRequest(roomId, GetSessionId(), isTyping));
    }

    public async Task<ReportRecord> ReportMessage(string roomId, string messageId, string reason)
    {
        var report = await _reportingService.ReportMessageAsync(
            new ReportMessageRequest(roomId, messageId, GetSessionId(), reason, GetIpAddress()));

        await Clients.Caller.ReportAccepted(report);
        return report;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionId = GetSessionId();
        var roomIds = _connectionRoomTracker.RemoveConnection(Context.ConnectionId);

        foreach (var roomId in roomIds)
        {
            await _roomService.LeaveRoomAsync(new LeaveRoomRequest(roomId, sessionId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string GetSessionId()
    {
        return Context.GetHttpContext()?.Request.Cookies[AnonymousSessionCookieMiddleware.CookieName]
            ?? Context.ConnectionId;
    }

    private string? GetIpAddress()
    {
        return Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
    }
}
