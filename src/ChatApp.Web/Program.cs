using System.Threading.RateLimiting;
using ChatApp.Application;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Options;
using ChatApp.Infrastructure;
using ChatApp.Web.Components;
using ChatApp.Web.Health;
using ChatApp.Web.Hubs;
using ChatApp.Web.Realtime;
using ChatApp.Web.Security;
using ChatApp.Web.Session;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace ChatApp.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<ChatRuntimeOptions>(builder.Configuration.GetSection(ChatRuntimeOptions.SectionName));

        builder.Services.AddChatApplication();
        builder.Services.AddChatInfrastructure(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ClientSessionContext>();
        builder.Services.AddScoped<AdminAccessService>();
        builder.Services.AddSingleton<ChatEventBroker>();
        builder.Services.AddSingleton<IChatEventPublisher>(provider => provider.GetRequiredService<ChatEventBroker>());
        builder.Services.AddSingleton<IChatEventFeed>(provider => provider.GetRequiredService<ChatEventBroker>());
        builder.Services.AddSingleton<ConnectionRoomTracker>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddSignalR();
        builder.Services.AddHealthChecks()
            .AddCheck<TemporaryStoreHealthCheck>("temporary-store");
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 180,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseMiddleware<AnonymousSessionCookieMiddleware>();
        app.UseRateLimiter();
        app.UseAntiforgery();

        app.MapPost("/admin/auth", async (HttpContext context, AdminAccessService adminAccessService) =>
        {
            var form = await context.Request.ReadFormAsync();
            var passcode = form["passcode"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            if (adminAccessService.ValidatePasscode(passcode))
            {
                adminAccessService.SignIn(context);
                return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/admin" : returnUrl);
            }

            return Results.Redirect("/admin?error=invalid");
        }).DisableAntiforgery();

        app.MapPost("/admin/logout", (HttpContext context, AdminAccessService adminAccessService) =>
        {
            adminAccessService.SignOut(context);
            return Results.Redirect("/admin");
        }).DisableAntiforgery();

        app.MapHealthChecks("/health");
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.RunAsync();
    }
}
