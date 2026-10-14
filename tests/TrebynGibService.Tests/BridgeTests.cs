using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TrebynGibService.Tests;

public sealed class BridgeTests : IDisposable
{
    private const string ClientId = "ifs-demo";
    private const string Secret = "s3cret";
    private const string Taxpayer = "8600897228";

    private readonly FakeGibHandler gib = new();
    private readonly WebApplicationFactory<Program> factory;

    public BridgeTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gib:BaseUrl"] = "https://gib.test",
                ["Gib:Token"] = "gib-token",
                ["Gib:Integrator"] = "8920229035-IFS KURUMSAL",
                ["Gib:PublicIpEchoUrl"] = "",
                ["Bridge:BlockedPathFragments:0"] = "onayla",
                ["Bridge:Clients:0:Id"] = ClientId,
                ["Bridge:Clients:0:SecretSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))),
                ["Bridge:Clients:0:TaxpayerIds:0"] = Taxpayer,
            }));
            builder.ConfigureServices(services =>
                services.AddHttpClient<GibForwarder>().ConfigurePrimaryHttpMessageHandler(() => gib));
        });
    }

    public void Dispose() => factory.Dispose();

    private HttpClient CreateClient(string? secret = Secret)
    {
        var client = factory.CreateClient();
        if (secret is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{secret}")));
        }
        return client;
    }

    private static object Envelope(string method, string path, string taxpayer = Taxpayer, object? body = null) =>
        new { method, path, taxpayerId = taxpayer, body };

    [Fact]
    public async Task Forwards_call_with_integrator_headers_and_returns_gib_status_and_body()
    {
        gib.Respond(HttpStatusCode.UnprocessableEntity, """{"messages":{"errorMessages":[{"message":"hata"}]}}""");

        var response = await CreateClient().PostAsJsonAsync("/api/gib",
            Envelope("DELETE", "/kdv1/beyanname/toplu/kismiSil", body: new { beyannameId = "467735", sekmeAdi = "MATRAH" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(422, result.GetProperty("status").GetInt32());
        Assert.Equal("hata", result.GetProperty("body").GetProperty("messages").GetProperty("errorMessages")[0].GetProperty("message").GetString());

        var sent = Assert.Single(gib.Requests);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("https://gib.test/kdv1/beyanname/toplu/kismiSil", sent.Uri);
        Assert.Equal("Bearer gib-token", sent.Headers["Authorization"]);
        Assert.Equal("ENTEGRATOR", sent.Headers["CHANNEL"]);
        Assert.Equal("8920229035-IFS KURUMSAL", sent.Headers["ENTEGRATOR"]);
        Assert.Equal(Taxpayer, sent.Headers["MUKELLEF-VKN"]);
        Assert.Equal("""{"beyannameId":"467735","sekmeAdi":"MATRAH"}""", sent.Body);
    }

    [Fact]
    public async Task Keeps_query_string_and_sends_no_body_on_get()
    {
        gib.Respond(HttpStatusCode.OK, """{"data":"TASLAK"}""");

        var response = await CreateClient().PostAsJsonAsync("/api/gib", Envelope("GET", "/kdv1/beyanname/durum?beyannameId=467735"));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TASLAK", result.GetProperty("body").GetProperty("data").GetString());
        var sent = Assert.Single(gib.Requests);
        Assert.Equal("https://gib.test/kdv1/beyanname/durum?beyannameId=467735", sent.Uri);
        Assert.Null(sent.Body);
    }

    [Fact]
    public async Task Passes_a_non_json_gib_answer_on_as_text()
    {
        gib.Respond(HttpStatusCode.BadGateway, "<html>gateway error</html>");

        var response = await CreateClient().PostAsJsonAsync("/api/gib", Envelope("GET", "/kdv1/beyanname/durum?beyannameId=1"));

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(502, result.GetProperty("status").GetInt32());
        Assert.Equal("<html>gateway error</html>", result.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Rejects_unknown_client(string? secret)
    {
        var response = await CreateClient(secret).PostAsJsonAsync("/api/gib", Envelope("GET", "/kdv1/beyanname/durum"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(gib.Requests);
    }

    [Fact]
    public async Task Rejects_taxpayer_the_client_may_not_file_for()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/gib", Envelope("GET", "/kdv1/beyanname/durum", taxpayer: "1111111111"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(gib.Requests);
    }

    [Theory]
    [InlineData("POST", "/admin/users")]
    [InlineData("POST", "https://evil.example/kdv1/beyanname")]
    [InlineData("POST", "/kdv1/../admin")]
    [InlineData("PATCH", "/kdv1/beyanname/toplu/onayla")]
    [InlineData("TRACE", "/kdv1/beyanname")]
    public async Task Rejects_paths_and_methods_that_are_not_allowed(string method, string path)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/gib", Envelope(method, path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gib.Requests);
    }

    [Fact]
    public async Task Gib_check_reports_reachable_when_gib_accepts_the_call()
    {
        gib.Respond(HttpStatusCode.OK, """{"data":[{"versiyon":44}]}""");

        var response = await CreateClient().GetAsync("/health/gib");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("gibReachable").GetBoolean());
        var sent = Assert.Single(gib.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://gib.test/kdv1/beyanname/versiyonlar", sent.Uri);
        Assert.Equal(Taxpayer, sent.Headers["MUKELLEF-VKN"]);
    }

    [Fact]
    public async Task Gib_check_returns_503_with_gib_answer_when_gib_refuses()
    {
        gib.Respond(HttpStatusCode.Forbidden, """{"message":"IP not allowed"}""");

        var response = await CreateClient().GetAsync("/health/gib");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("gibReachable").GetBoolean());
        Assert.Equal(403, result.GetProperty("gibStatus").GetInt32());
        Assert.Contains("IP not allowed", result.GetProperty("gibAnswer").GetString());
    }

    [Fact]
    public async Task Gib_check_needs_client_credentials()
    {
        var response = await CreateClient(secret: null).GetAsync("/health/gib");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(gib.Requests);
    }

    [Fact]
    public async Task Health_needs_no_credentials()
    {
        var response = await CreateClient(secret: null).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

internal sealed record SentRequest(HttpMethod Method, string Uri, Dictionary<string, string> Headers, string? Body);

internal sealed class FakeGibHandler : HttpMessageHandler
{
    private HttpStatusCode status = HttpStatusCode.OK;
    private string content = "{}";

    public List<SentRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode status, string content)
    {
        this.status = status;
        this.content = content;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(request.Method, request.RequestUri!.ToString(), headers, body));
        return new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8) };
    }
}
