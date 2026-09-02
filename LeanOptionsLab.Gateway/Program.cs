using LeanOptionsLab.Gateway;

var command = GatewayCommandLine.Parse(args);
if (command.Kind == GatewayCommandKind.HealthProbe)
{
    return await GatewayHealthProbe.RunAsync();
}

if (command.Kind == GatewayCommandKind.Invalid)
{
    Console.Error.WriteLine("Usage: LeanOptionsLab.Gateway [--health-probe]");
    return 64;
}

var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenAnyIP(8080);
});

var resultsRoot = Environment.GetEnvironmentVariable("LEAN_OPTIONS_LAB_RESULTS_ROOT") ?? "/results";
builder.Services.AddSingleton(new GatewayStateReader(resultsRoot));

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }

    await next(context);
});

app.MapGet("/", () => Results.Ok(new
{
    service = "LeanOptionsLab status gateway",
    mode = "read-only",
    status = "api/v1/status",
    health = "healthz",
    readiness = "readyz"
}));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/readyz", (GatewayStateReader reader) =>
{
    if (!reader.TryReadLatest(out var status))
    {
        return Results.Json(
            new { status = "unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "ready",
        experimentReady = status!.ExperimentReady
    });
});

app.MapGet("/api/v1/status", (GatewayStateReader reader) =>
{
    if (!reader.TryReadLatest(out var status))
    {
        return Results.Json(
            new { status = "unavailable" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(status);
});

await app.RunAsync();
return 0;
