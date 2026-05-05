using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Exceptions;
using ChatApp.Application.Options;
using ChatApp.Application.Services;
using ChatApp.Infrastructure.Abuse;
using ChatApp.Infrastructure.Moderation;
using ChatApp.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace ChatApp.Tests;

public class ChatFlowTests
{
    [Fact]
    public async Task CreateRoomAsync_AddsRoomToListedCatalogue()
    {
        var fixture = new TestFixture();

        var room = await fixture.RoomService.CreateRoomAsync(new CreateRoomRequest(
            "Launch Lounge",
            RoomVisibility.Listed,
            "session-1",
            "127.0.0.1"));

        Assert.Equal(6, room.Id.Length);
        Assert.Equal("Launch Lounge", room.Name);

        var listedRooms = await fixture.RoomService.GetListedRoomsAsync();
        var listedRoom = Assert.Single(listedRooms);
        Assert.Equal(room.Id, listedRoom.Id);
        Assert.Equal(0, listedRoom.ParticipantCount);
    }

    [Fact]
    public async Task JoinAndSendMessageAsync_ReturnsSnapshotAndMessageHistory()
    {
        var fixture = new TestFixture();
        var room = await fixture.RoomService.CreateRoomAsync(new CreateRoomRequest("Team Chat", RoomVisibility.Listed, "session-1", "127.0.0.1"));

        var snapshot = await fixture.RoomService.JoinRoomAsync(new JoinRoomRequest(room.Id, "Guest Fox", "session-1", "127.0.0.1"));
        await fixture.TypingService.SetTypingAsync(new SetTypingRequest(room.Id, "session-1", true));
        var message = await fixture.MessageService.SendMessageAsync(new SendMessageRequest(room.Id, "session-1", "Hello from the temp room", "127.0.0.1"));

        Assert.NotNull(snapshot.CurrentParticipant);
        Assert.Equal("Guest Fox", snapshot.CurrentParticipant!.Nickname);
        Assert.Single(snapshot.Participants);
        Assert.Equal("Hello from the temp room", message.Content);

        var messages = await fixture.MessageService.GetRecentMessagesAsync(room.Id);
        var storedMessage = Assert.Single(messages);
        Assert.Equal(message.Id, storedMessage.Id);

        var typingIndicators = await fixture.TypingService.GetTypingIndicatorsAsync(room.Id);
        Assert.Empty(typingIndicators);
    }

    [Fact]
    public async Task MuteParticipantAsync_BlocksFutureMessages()
    {
        var fixture = new TestFixture();
        var room = await fixture.RoomService.CreateRoomAsync(new CreateRoomRequest("Quiet Room", RoomVisibility.Private, "session-1", "127.0.0.1"));
        await fixture.RoomService.JoinRoomAsync(new JoinRoomRequest(room.Id, "Muted Otter", "session-1", "127.0.0.1"));

        await fixture.ModerationService.MuteParticipantAsync(new MuteParticipantRequest(room.Id, "session-1", "admin", "Muted for test"));

        var exception = await Assert.ThrowsAsync<ChatDomainException>(() =>
            fixture.MessageService.SendMessageAsync(new SendMessageRequest(room.Id, "session-1", "Can anyone hear me?", "127.0.0.1")));

        Assert.Equal("participant_muted", exception.ErrorCode);
    }

    [Fact]
    public async Task CloseRoomAsync_PreventsNewJoins()
    {
        var fixture = new TestFixture();
        var room = await fixture.RoomService.CreateRoomAsync(new CreateRoomRequest("Moderated Room", RoomVisibility.Listed, "session-1", "127.0.0.1"));

        await fixture.ModerationService.CloseRoomAsync(new CloseRoomRequest(room.Id, "admin", "Closing for test"));

        var exception = await Assert.ThrowsAsync<ChatDomainException>(() =>
            fixture.RoomService.JoinRoomAsync(new JoinRoomRequest(room.Id, "Late Guest", "session-2", "127.0.0.1")));

        Assert.Equal("room_closed", exception.ErrorCode);
    }

    [Fact]
    public async Task OldMessagesExpireWhileRoomStaysActive()
    {
        var fixture = new TestFixture();
        var room = await fixture.RoomService.CreateRoomAsync(new CreateRoomRequest("TTL Room", RoomVisibility.Listed, "session-1", "127.0.0.1"));
        await fixture.RoomService.JoinRoomAsync(new JoinRoomRequest(room.Id, "Clock Owl", "session-1", "127.0.0.1"));

        await fixture.MessageService.SendMessageAsync(new SendMessageRequest(room.Id, "session-1", "first", "127.0.0.1"));
        fixture.TimeProvider.Advance(TimeSpan.FromHours(23));
        await fixture.RoomService.JoinRoomAsync(new JoinRoomRequest(room.Id, "Clock Owl", "session-1", "127.0.0.1"));
        await fixture.MessageService.SendMessageAsync(new SendMessageRequest(room.Id, "session-1", "second", "127.0.0.1"));
        fixture.TimeProvider.Advance(TimeSpan.FromHours(2));

        var messages = await fixture.MessageService.GetRecentMessagesAsync(room.Id);
        var survivingMessage = Assert.Single(messages);
        Assert.Equal("second", survivingMessage.Content);
        Assert.NotNull(await fixture.RoomService.GetRoomAsync(room.Id));
    }

    private sealed class TestFixture
    {
        private readonly IOptions<ChatRuntimeOptions> _options;
        private readonly IChatEventPublisher _eventPublisher;
        private readonly IBannedContentPolicy _bannedContentPolicy;
        private readonly IActionThrottle _actionThrottle;

        public TestFixture()
        {
            TimeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 04, 15, 12, 0, 0, TimeSpan.Zero));
            Store = new InMemoryTemporaryChatStore(TimeProvider);

            var options = new ChatRuntimeOptions
            {
                BannedTerms = ["blockedword"]
            };

            _options = Options.Create(options);
            _eventPublisher = new RecordingEventPublisher();
            _bannedContentPolicy = new ListBasedBannedContentPolicy(_options);
            _actionThrottle = new InMemoryActionThrottle(TimeProvider);

            RoomService = new RoomService(Store, _eventPublisher, _bannedContentPolicy, _actionThrottle, _options, TimeProvider);
            MessageService = new MessageService(Store, _eventPublisher, _bannedContentPolicy, _actionThrottle, _options, TimeProvider);
            PresenceService = new PresenceService(Store, _eventPublisher, _options, TimeProvider);
            TypingService = new TypingService(Store, _eventPublisher, _options, TimeProvider);
            ReportingService = new ReportingService(Store, _eventPublisher, _actionThrottle, _options, TimeProvider);
            ModerationService = new ModerationService(Store, _eventPublisher, _options, TimeProvider);
        }

        public MutableTimeProvider TimeProvider { get; }
        public InMemoryTemporaryChatStore Store { get; }
        public IRoomService RoomService { get; }
        public IMessageService MessageService { get; }
        public IPresenceService PresenceService { get; }
        public ITypingService TypingService { get; }
        public IReportingService ReportingService { get; }
        public IModerationService ModerationService { get; }
    }

    private sealed class RecordingEventPublisher : IChatEventPublisher
    {
        public List<ChatEvent> PublishedEvents { get; } = [];

        public Task PublishAsync(ChatEvent chatEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(chatEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }
}
