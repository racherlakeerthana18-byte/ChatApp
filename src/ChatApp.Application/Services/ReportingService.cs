using ChatApp.Application.Abstractions;
using ChatApp.Application.Contracts;
using ChatApp.Application.Exceptions;
using ChatApp.Application.Internal;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Application.Services;

public sealed class ReportingService : IReportingService
{
    private readonly ITemporaryChatStore _store;
    private readonly IChatEventPublisher _events;
    private readonly IActionThrottle _throttle;
    private readonly ChatRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;

    public ReportingService(
        ITemporaryChatStore store,
        IChatEventPublisher events,
        IActionThrottle throttle,
        IOptions<ChatRuntimeOptions> options,
        TimeProvider timeProvider)
    {
        _store = store;
        _events = events;
        _throttle = throttle;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<ReportRecord>> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetReportsAsync(cancellationToken);
    }

    public async Task<ReportRecord> ReportMessageAsync(ReportMessageRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = ChatInput.NormalizeRoomCode(request.RoomId);
        _ = await ChatGuards.GetRequiredRoomAsync(_store, roomId, cancellationToken);

        await _throttle.EnforceLimitAsync(
            "report-session",
            request.SessionId,
            _options.ReportLimitPerMinute,
            _options.ReportWindow,
            cancellationToken);

        await _throttle.EnforceLimitAsync(
            "report-ip",
            ChatInput.SafeKey(request.IpAddress),
            _options.ReportLimitPerMinute,
            _options.ReportWindow,
            cancellationToken);

        var recentMessages = await _store.GetMessagesAsync(
            roomId,
            _timeProvider.GetUtcNow().Subtract(_options.MessageTtl),
            _options.MaxMessageHistory,
            cancellationToken);

        if (recentMessages.All(message => message.Id != request.MessageId))
        {
            throw new ChatDomainException("That message is no longer available to report.", "message_not_found");
        }

        var report = new ReportRecord(
            Guid.NewGuid().ToString("N"),
            roomId,
            request.MessageId,
            request.SessionId,
            ChatInput.NormalizeReason(request.Reason, "No reason supplied."),
            _timeProvider.GetUtcNow());

        await _store.AddReportAsync(report, _options.RoomTtl, cancellationToken);
        await _events.PublishAsync(new ReportAcceptedEvent(report), cancellationToken);

        return report;
    }
}
