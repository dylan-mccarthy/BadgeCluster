using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using BadgeCluster.Api;
using Microsoft.AspNetCore.Http.Timeouts;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2048);
builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy
    { Timeout = TimeSpan.FromSeconds(10), TimeoutStatusCode = 503 });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter(
            "global", _ => new() { PermitLimit = 100, Window = TimeSpan.FromSeconds(1), QueueLimit = 0 })),
        PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "30";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "rate_limited" }, ct);
    };
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var options = new TableClientOptions();
    options.Retry.MaxRetries = 1;
    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(3);
    var tableName = config["Storage:TableName"] ?? "BadgeCluster";
    var connection = config["Storage:ConnectionString"];
    if (!string.IsNullOrEmpty(connection)) return new TableClient(connection, tableName, options);
    var endpoint = config["Storage:TableEndpoint"]
        ?? throw new InvalidOperationException("Configure Storage:TableEndpoint or Storage:ConnectionString");
    return new TableClient(new Uri(endpoint), tableName, new DefaultAzureCredential(), options);
});
builder.Services.AddSingleton<TableStore>();
var app = builder.Build();
using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
    await app.Services.GetRequiredService<TableStore>().Initialize(startup.Token);

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (context.Request.ContentLength > 2048)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        await context.Response.WriteAsJsonAsync(new { error = "request_too_large" });
        return;
    }
    try { await next(context); }
    catch (ContributionConflict ex) when (!context.Response.HasStarted)
    { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (Exception ex) when (ex is RequestFailedException or StoreBusyException && !context.Response.HasStarted)
    {
        app.Logger.LogWarning("Storage unavailable ({Category})", ex.GetType().Name);
        context.Response.StatusCode = 503;
        context.Response.Headers.RetryAfter = "30";
        await context.Response.WriteAsJsonAsync(new { error = "storage_unavailable" });
    }
});
app.UseRequestTimeouts();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPost("/api/v1/contributions", async (Contribution input, TableStore store, CancellationToken ct) =>
{
    if (input.Validate() is { } error) return Results.BadRequest(new { error });
    return Results.Ok(await store.Submit(input, ct));
});
app.MapGet("/api/v1/cluster", async (TableStore store, CancellationToken ct) => Results.Ok(await store.Snapshot(ct)));
app.MapGet("/health", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (TableClient table, CancellationToken ct) =>
{
    await table.GetEntityIfExistsAsync<Counters>("v1", "totals", cancellationToken: ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();
public partial class Program;
