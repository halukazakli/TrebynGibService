using System.Text.RegularExpressions;

namespace TrebynGibService;

public static partial class RequestValidator
{
    private static readonly HashSet<string> Methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    /// <returns>An error message, or null when the request may be forwarded.</returns>
    public static string? Validate(GibRequest request, BridgeOptions options)
    {
        if (request.Method is null || !Methods.Contains(request.Method.ToUpperInvariant()))
        {
            return "Method must be one of GET, POST, PUT, PATCH, DELETE.";
        }

        if (string.IsNullOrEmpty(request.TaxpayerId) || !IsTaxpayerId(request.TaxpayerId))
        {
            return "TaxpayerId must be a 10 digit VKN or 11 digit TCKN.";
        }

        var path = request.Path;
        if (string.IsNullOrEmpty(path) || !path.StartsWith('/') || path.StartsWith("//")
            || path.Contains("..") || path.Contains('\\') || path.Contains("://"))
        {
            return "Path must be a relative GIB API path starting with '/'.";
        }

        var route = path.Split('?')[0];
        if (!options.AllowedPathPrefixes.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return "Path is not an allowed GIB API path.";
        }

        if (options.BlockedPathFragments.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "This operation is disabled on this bridge.";
        }

        return null;
    }

    public static bool IsTaxpayerId(string value) => TaxpayerIdPattern().IsMatch(value);

    [GeneratedRegex(@"^\d{10,11}$")]
    private static partial Regex TaxpayerIdPattern();
}
