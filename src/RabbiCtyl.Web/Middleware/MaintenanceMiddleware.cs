using System.Text;
using System.Text.Encodings.Web;
using RabbiCtyl.Configuration.Options;
using RabbiCtyl.Core;
using RabbiCtyl.Infrastructure.Maintenance;

namespace RabbiCtyl.Web.Middleware;

/// <summary>
/// Blocks the panel while <see cref="MaintenanceState"/> reports that the
/// database is being created or migrated, and shows a page with the schema
/// versions, details and an ETA. <c>/health</c> stays reachable.
/// </summary>
public sealed class MaintenanceMiddleware
{
    private readonly RequestDelegate _next;

    public MaintenanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        MaintenanceState maintenance,
        PanelOptions options)
    {
        var snapshot = maintenance.Current;

        if (!snapshot.BlocksRequests || context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.RetryAfter = "10";

        await context.Response.WriteAsync(Render(snapshot, options.App.Name), Encoding.UTF8);
    }

    private static string Render(MaintenanceSnapshot snapshot, string panelName)
    {
        static string encode(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);
        var title = snapshot.Phase switch
        {
            MaintenancePhase.Migrating => "Database migration in progress",
            MaintenancePhase.Failed => "Database maintenance failed",
            _ => "Panel is starting",
        };

        var detail = snapshot.Phase switch
        {
            MaintenancePhase.Migrating => "Migration in progress of your database. The panel will reopen automatically.",
            MaintenancePhase.Failed => snapshot.Detail ?? "The panel could not prepare the database.",
            _ => snapshot.Detail ?? "Preparing the database.",
        };

        var eta = snapshot.Phase switch
        {
            MaintenancePhase.Migrating =>
                $"<p class=\"eta\">Estimated completion in about {Math.Max(1, (int)Math.Ceiling(snapshot.Remaining.TotalSeconds))} seconds.</p>",
            MaintenancePhase.Failed => string.Empty,
            _ => "<p class=\"eta\">This usually takes a few seconds.</p>",
        };

        var error = string.IsNullOrWhiteSpace(snapshot.Error)
            ? string.Empty
            : $"<pre class=\"error\">{encode(snapshot.Error)}</pre>";

        var refresh = snapshot.Phase == MaintenancePhase.Failed
            ? string.Empty
            : "<meta http-equiv=\"refresh\" content=\"5\" />";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta name="robots" content="noindex" />
  {{refresh}}
  <title>{{encode(title)}} · {{encode(panelName)}}</title>
  <style>
    :root { color-scheme: dark; }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 24px;
      background: #0a0c10;
      color: #e7ecf4;
      font-family: "Inter", "Segoe UI", system-ui, sans-serif;
    }
    .card {
      width: min(560px, 100%);
      border: 1px solid #242c38;
      border-radius: 14px;
      background: #11151b;
      padding: 32px;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 22px;
      color: #ff6a3d;
      font-weight: 700;
      letter-spacing: 0.02em;
    }
    .dot {
      width: 9px;
      height: 9px;
      border-radius: 999px;
      background: currentColor;
      animation: pulse 1.4s ease-in-out infinite;
    }
    @keyframes pulse { 50% { opacity: 0.35; } }
    h1 { margin: 0 0 10px; font-size: 22px; }
    p { margin: 0 0 10px; color: #8b97a9; line-height: 1.55; }
    .eta { color: #e7ecf4; font-weight: 600; }
    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 6px 16px;
      margin: 20px 0 0;
      padding-top: 18px;
      border-top: 1px solid #242c38;
      font-size: 13px;
    }
    dt { color: #8b97a9; }
    dd { margin: 0; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .error {
      margin-top: 18px;
      padding: 12px;
      border-radius: 8px;
      background: rgb(255 115 115 / 10%);
      color: #ff7373;
      white-space: pre-wrap;
      font-size: 12px;
    }
  </style>
</head>
<body>
  <main class="card">
    <div class="brand"><span class="dot"></span>{{encode(panelName)}}</div>
    <h1>{{encode(title)}}</h1>
    <p>{{encode(detail)}}</p>
    {{eta}}
    <dl>
      <dt>Schema version</dt>
      <dd>{{snapshot.FromVersion}} → {{snapshot.ToVersion}} (panel expects {{PanelSchema.CurrentVersion}})</dd>
      <dt>Started (UTC)</dt>
      <dd>{{snapshot.StartedAt:yyyy-MM-dd HH:mm:ss}}</dd>
      <dt>Phase</dt>
      <dd>{{snapshot.Phase}}</dd>
    </dl>
    {{error}}
  </main>
</body>
</html>
""";
    }
}
