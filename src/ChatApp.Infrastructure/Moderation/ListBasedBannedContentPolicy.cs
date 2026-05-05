using ChatApp.Application.Abstractions;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure.Moderation;

public sealed class ListBasedBannedContentPolicy : IBannedContentPolicy
{
    private readonly string[] _blockedTerms;

    public ListBasedBannedContentPolicy(IOptions<ChatRuntimeOptions> options)
    {
        _blockedTerms = options.Value.BannedTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool ContainsBlockedContent(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || _blockedTerms.Length == 0)
        {
            return false;
        }

        return _blockedTerms.Any(term => input.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
