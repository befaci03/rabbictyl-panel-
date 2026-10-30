using System.Net;
using Microsoft.AspNetCore.Http;
using RabbiCtyl.Core.Abstractions;

namespace RabbiCtyl.Web.Middleware;

/// <summary>
/// Serves the operator-maintenance page while an administrator has maintenance
/// mode enabled from Administration → General. Administrators and the sign-in
/// page stay reachable so the mode can be switched off again.
/// </summary>
public sealed class OperatorMaintenanceMiddleware
{
    private readonly RequestDelegate _next;

    public OperatorMaintenanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPanelBrandingStore branding)
    {
        var current = branding.Current;

        if (!current.MaintenanceMode || context.User.IsInRole("admin"))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;

        if (path.StartsWithSegments("/auth/login") ||
            path.StartsWithSegments("/auth/logout") ||
            path.StartsWithSegments("/assets") ||
            path.StartsWithSegments("/themes") ||
            path.StartsWithSegments("/addons") ||
            path.StartsWithSegments("/panel/logo") ||
            path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/error"))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.RetryAfter = "120";

        var reason = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(current.MaintenanceReason)
                ? "The panel is under maintenance. Please try again shortly."
                : current.MaintenanceReason);
        var name = WebUtility.HtmlEncode(current.Name);

        await context.Response.WriteAsync($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Maintenance · {name}</title>
                <link rel="stylesheet" href="/assets/css/app.css" />
            </head>
            <body>
                <main class="content content--bare">
                    <section class="card" style="max-width: 560px; margin: 12vh auto; padding: 32px;">
                        <span class="eyebrow">Maintenance</span>
                        <h1 class="page__title">{name} is under maintenance</h1>
                        <p class="page__subtitle">{reason}</p>
                        <p class="form__hint">Administrators can still sign in.</p>
                        <a class="button button--ghost" href="/auth/login">Sign in</a>
                    </section>
                </main>
            </body>
            </html>
            """);
    }
}
