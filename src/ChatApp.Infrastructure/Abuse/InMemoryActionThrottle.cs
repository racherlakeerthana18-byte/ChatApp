using ChatApp.Application.Abstractions;
using ChatApp.Application.Exceptions;

namespace ChatApp.Infrastructure.Abuse;

public sealed class InMemoryActionThrottle : IActionThrottle
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _eventsByKey = [];
    private readonly TimeProvider _timeProvider;

    public InMemoryActionThrottle(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task EnforceLimitAsync(string scope, string key, int maxEvents, TimeSpan window, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var compositeKey = $"{scope}:{key}";

        lock (_lock)
        {
            if (!_eventsByKey.TryGetValue(compositeKey, out var queue))
            {
                queue = new Queue<DateTimeOffset>();
                _eventsByKey[compositeKey] = queue;
            }

            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= maxEvents)
            {
                throw new ChatDomainException("Too many requests. Please slow down for a moment.", "rate_limited");
            }

            queue.Enqueue(now);
        }

        return Task.CompletedTask;
    }
}
