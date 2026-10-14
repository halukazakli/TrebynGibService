using System.Diagnostics;
using Microsoft.Extensions.Options;
using TrebynGibService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GibOptions>(builder.Configuration.GetSection(GibOptions.Section));
builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection(BridgeOptions.Section));
builder.Services.AddSingleton<ClientAuthenticator>();
builder.Services.AddTransient<GibConnectivityCheck>();
builder.Services.AddHttpClient<GibForwarder>((services, http) =>
{
    var gib = services.GetRequiredService<IOptions<GibOptions>>().Value;
    http.BaseAddress = new Uri(gib.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(gib.TimeoutSeconds);
});
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok("OK"));

// Real GIB round trip with the integrator credentials: 200 when GIB accepts this
// server, 503 otherwise. Needs client credentials so it cannot be used anonymously.
app.MapGet("/health/gib", async (
    string? taxpayerId,
    HttpRequest http,
    ClientAuthenticator authenticator,
    IOptions<GibOptions> gibOptions,
    GibConnectivityCheck check,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var client = authenticator.Authenticate(http);
    if (client is null)
    {
        return Results.Unauthorized();
    }

    var taxpayer = taxpayerId
        ?? (string.IsNullOrEmpty(gibOptions.Value.ProbeTaxpayerId) ? null : gibOptions.Value.ProbeTaxpayerId)
        ?? client.TaxpayerIds.FirstOrDefault(id => id != "*");
    if (taxpayer is null || !RequestValidator.IsTaxpayerId(taxpayer))
    {
        return Results.Problem("Give a taxpayerId (10 digit VKN / 11 digit TCKN) or configure Gib:ProbeTaxpayerId.", statusCode: StatusCodes.Status400BadRequest);
    }
    if (!ClientAuthenticator.MayFileFor(client, taxpayer))
    {
        return Results.Problem($"Client is not allowed to file for taxpayer {taxpayer}.", statusCode: StatusCodes.Status403Forbidden);
    }

    var result = await check.RunAsync(taxpayer, cancellationToken);
    logger.LogInformation("GIB connectivity check by {Client}: reachable={Reachable} status={Status} publicIp={PublicIp}",
        client.Id, result.GibReachable, result.GibStatus, result.PublicIp);
    return Results.Json(result, statusCode: result.GibReachable ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/gib", async (
    GibRequest request,
    HttpRequest http,
    ClientAuthenticator authenticator,
    IOptionsMonitor<BridgeOptions> bridgeOptions,
    GibForwarder forwarder,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var client = authenticator.Authenticate(http);
    if (client is null)
    {
        return Results.Unauthorized();
    }

    var options = bridgeOptions.CurrentValue;
    var error = RequestValidator.Validate(request, options);
    if (error is not null)
    {
        logger.LogWarning("Rejected request from {Client}: {Error} ({Method} {Path})", client.Id, error, request.Method, request.Path);
        return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    if (!ClientAuthenticator.MayFileFor(client, request.TaxpayerId!))
    {
        logger.LogWarning("Client {Client} is not allowed to file for taxpayer {Taxpayer}", client.Id, request.TaxpayerId);
        return Results.Problem($"Client is not allowed to file for taxpayer {request.TaxpayerId}.", statusCode: StatusCodes.Status403Forbidden);
    }

    if (options.LogBodies)
    {
        logger.LogInformation("Request body {Method} {Path}: {Body}", request.Method, request.Path, request.Body?.GetRawText());
    }

    var watch = Stopwatch.StartNew();
    GibResponse response;
    try
    {
        response = await forwarder.ForwardAsync(request, cancellationToken);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        logger.LogError(ex, "GIB call failed for {Client}/{Taxpayer}: {Method} {Path}", client.Id, request.TaxpayerId, request.Method, request.Path);
        return Results.Problem($"GIB could not be reached: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }

    logger.LogInformation("{Client}/{Taxpayer} {Method} {Path} -> {Status} in {Elapsed} ms",
        client.Id, request.TaxpayerId, request.Method, request.Path, response.Status, watch.ElapsedMilliseconds);
    if (options.LogBodies)
    {
        logger.LogInformation("Response body {Method} {Path}: {Body}", request.Method, request.Path, response.Body?.GetRawText());
    }

    return Results.Ok(response);
});

app.Run();

public partial class Program;
