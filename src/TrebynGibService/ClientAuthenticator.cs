using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TrebynGibService;

/// <summary>Resolves the calling IFS installation from its HTTP Basic credentials.</summary>
public sealed class ClientAuthenticator(IOptionsMonitor<BridgeOptions> options)
{
    public BridgeClient? Authenticate(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
            || !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return null;
        }

        var separator = decoded.IndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        var id = decoded[..separator];
        var secret = decoded[(separator + 1)..];
        var client = options.CurrentValue.Clients.FirstOrDefault(c => c.Enabled && c.Id == id);
        if (client is null)
        {
            return null;
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(client.SecretSha256);
        }
        catch (FormatException)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expected) ? client : null;
    }

    public static bool MayFileFor(BridgeClient client, string taxpayerId) =>
        client.TaxpayerIds.Contains("*") || client.TaxpayerIds.Contains(taxpayerId);
}
