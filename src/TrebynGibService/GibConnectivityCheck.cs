using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace TrebynGibService;

public sealed record GibConnectivityResult(
    bool GibReachable,
    int? GibStatus,
    long ElapsedMs,
    string Endpoint,
    string? GibAnswer,
    string? Error,
    string? PublicIp);

/// <summary>
/// Makes one harmless read-only GIB call with the integrator credentials, so a
/// freshly installed bridge shows whether GIB accepts this server (IP + token).
/// </summary>
public sealed class GibConnectivityCheck(GibForwarder forwarder, IHttpClientFactory httpFactory, IOptions<GibOptions> options)
{
    private const string ProbePath = "/kdv1/beyanname/versiyonlar";

    public async Task<GibConnectivityResult> RunAsync(string taxpayerId, CancellationToken cancellationToken)
    {
        var publicIp = await GetPublicIpAsync(cancellationToken);
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await forwarder.ForwardAsync(new GibRequest("GET", ProbePath, taxpayerId, null), cancellationToken);
            var answer = response.Body?.GetRawText();
            return new GibConnectivityResult(
                response.Status == 200, response.Status, watch.ElapsedMilliseconds, ProbePath,
                answer is { Length: > 1000 } ? answer[..1000] : answer, null, publicIp);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new GibConnectivityResult(false, null, watch.ElapsedMilliseconds, ProbePath, null,
                $"{ex.GetType().Name}: {ex.Message}", publicIp);
        }
    }

    private async Task<string?> GetPublicIpAsync(CancellationToken cancellationToken)
    {
        var url = options.Value.PublicIpEchoUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            return (await http.GetStringAsync(url, cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"unknown ({ex.Message})";
        }
    }
}
