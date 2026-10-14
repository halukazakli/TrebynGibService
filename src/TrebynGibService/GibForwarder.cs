using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TrebynGibService;

/// <summary>Makes the actual call to GIB with the integrator credentials.</summary>
public sealed class GibForwarder(HttpClient http, IOptions<GibOptions> options)
{
    public async Task<GibResponse> ForwardAsync(GibRequest request, CancellationToken cancellationToken)
    {
        var gib = options.Value;
        using var message = new HttpRequestMessage(new HttpMethod(request.Method!.ToUpperInvariant()), request.Path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gib.Token);
        message.Headers.Add("CHANNEL", gib.Channel);
        message.Headers.Add("ENTEGRATOR", gib.Integrator);
        message.Headers.Add("MUKELLEF-VKN", request.TaxpayerId);

        // GIB takes JSON bodies on DELETE too (kismiSil, beyanname silme).
        if (request.Body is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } body)
        {
            message.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new GibResponse((int)response.StatusCode, ToJson(text));
    }

    // GIB answers JSON almost always; anything else (e.g. a gateway error page) is
    // passed on as a JSON string so IFS can still show it.
    private static JsonElement? ToJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(text);
        }
    }
}
