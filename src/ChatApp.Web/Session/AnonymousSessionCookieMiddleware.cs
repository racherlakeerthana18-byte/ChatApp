namespace ChatApp.Web.Session;

public sealed class AnonymousSessionCookieMiddleware
{
    public const string CookieName = "chatapp-session";

    private readonly RequestDelegate _next;

    public AnonymousSessionCookieMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Cookies.ContainsKey(CookieName))
        {
            context.Response.Cookies.Append(
                CookieName,
                Guid.NewGuid().ToString("N"),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(7)
                });
        }

        await _next(context);
    }
}
