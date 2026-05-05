namespace ChatApp.Web.Session;

public sealed class ClientSessionContext
{
    public ClientSessionContext(IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        SessionId = httpContext?.Request.Cookies[AnonymousSessionCookieMiddleware.CookieName] ?? Guid.NewGuid().ToString("N");
        IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
    }

    public string SessionId { get; }
    public string? IpAddress { get; }
}
