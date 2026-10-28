using System.Reflection;
using BURAQ.Hubs;
using BURAQ.Middlewares;
using Scalar.AspNetCore;
using Serilog;

namespace BURAQ.Extensions.Startup;

internal static class AppExtensions
{
    private static readonly StaticFileOptions DefaultStaticFileOptions = new()
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.GetTypedHeaders().CacheControl = new()
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(7)
            };
        }
    };

    private static readonly WebSocketOptions DefaultWebSocketOptions =
        new() { KeepAliveInterval = TimeSpan.FromMinutes(30) };

    extension(WebApplication app)
    {
        internal async Task RunServerAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Server>>();

            try
            {
                var version = typeof(Server).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
                logger.SystemLog(version ?? "BURAQ", TaskStatus.Pending, LogLevel.Debug);
                await app.RunAsync();
            }
            catch (Exception exception)
            {
                logger.LogErrorMessage(exception, StaticLocalizer[nameof(Resources.Program.Server_Failed)]);
                throw;
            }
            finally
            {
                logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Server_Exited)], TaskStatus.Exit,
                    LogLevel.Debug);

                await Log.CloseAndFlushAsync();
            }
        }

        internal void UseMiddlewares()
        {
            app.UseRequestLocalization();

            // First in the pipeline so that every response — API, error page, static asset and
            // SPA fallback alike — carries the baseline browser headers (finding F-015).
            app.UseMiddleware<SecurityHeadersMiddleware>();

            app.UseResponseCaching();
            app.UseResponseCompression();

            app.UseCustomFavicon();
            app.UseStaticFiles(DefaultStaticFileOptions);

            app.UseForwardedHeaders();

            if (app.Environment.IsDevelopment())
            {
                // Development keeps its OpenAPI surfaces, but API failures must not echo stack
                // traces, request cookies, or other sensitive headers to browser clients.
                app.UseExceptionHandler("/error/500");
                app.UseOpenApi(options =>
                {
                    options.PostProcess += (document, _) => document.Servers.Clear();
                    options.Path = "/openapi/{documentName}.json";
                });
                // open ui in `/scalar/v1`
                app.MapScalarApiReference();
            }
            else
            {
                app.UseExceptionHandler("/error/500");
                app.UseHsts();
            }

            app.UseRouting();

            // Upgrades the compatibility cookie to BURAQ_Token. Must precede authentication.
            app.UseMiddleware<LegacyAuthCookieMiddleware>();

            app.UseAuthentication();

            // Global and named limiter partitions use the authenticated user ID when available.
            // Running this before authentication silently collapses every proxied caller into the
            // remote-address bucket and defeats the documented per-user isolation.
            if (app.Configuration.GetValue<bool>("DisableRateLimit") is not true)
                app.UseRateLimiter();

            app.UseAuthorization();

            // Explicit CSRF invariant for cookie-authenticated mutations (finding F-023).
            app.UseMiddleware<CookieOriginValidationMiddleware>();

            if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("RequestLogging"))
                app.UseRequestLogging();

            app.UseWebSockets(DefaultWebSocketOptions);
            app.UseTelemetry();

            app.MapHealthCheck();
            app.MapControllers();

            app.MapHub<UserHub>("/hub/user");
            app.MapHub<MonitorHub>("/hub/monitor");
            app.MapHub<AdminHub>("/hub/admin");
            app.MapHub<ZarrarHub>("/hub/zarrar");

            // One call, one fallback route. See UseSpaFallback's remarks: multiple MapFallback call
            // sites get cross-wired by the route interceptor source generator and produce
            // AmbiguousMatchException on every request.
            app.UseSpaFallback();
        }
    }
}
