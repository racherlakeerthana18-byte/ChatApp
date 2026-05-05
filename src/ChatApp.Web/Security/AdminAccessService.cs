using System.Security.Cryptography;
using System.Text;
using ChatApp.Application.Options;
using Microsoft.Extensions.Options;

namespace ChatApp.Web.Security;

public sealed class AdminAccessService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ChatRuntimeOptions _options;

    public AdminAccessService(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ChatRuntimeOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public bool IsAuthorized => HasValidCookie(_httpContextAccessor.HttpContext);

    public bool ValidatePasscode(string passcode)
    {
        if (string.IsNullOrWhiteSpace(passcode))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(_options.AdminPasscode);
        var actualBytes = Encoding.UTF8.GetBytes(passcode.Trim());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public bool HasValidCookie(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return false;
        }

        return httpContext.Request.Cookies.TryGetValue(_options.AdminCookieName, out var cookieValue)
            && string.Equals(cookieValue, _options.AdminCookieSecret, StringComparison.Ordinal);
    }

    public void SignIn(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append(
            _options.AdminCookieName,
            _options.AdminCookieSecret,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = httpContext.Request.IsHttps,
                MaxAge = TimeSpan.FromHours(8)
            });
    }

    public void SignOut(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(_options.AdminCookieName);
    }
}
