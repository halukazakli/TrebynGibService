using System.Text.Json;

namespace TrebynGibService;

/// <summary>What IFS sends: one GIB call to make on behalf of a taxpayer.</summary>
public sealed record GibRequest(string? Method, string? Path, string? TaxpayerId, JsonElement? Body);

/// <summary>
/// What IFS gets back, always with HTTP 200 once the call reached GIB:
/// GIB's own status code and response body, unchanged.
/// </summary>
public sealed record GibResponse(int Status, JsonElement? Body);
