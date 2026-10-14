namespace TrebynGibService;

/// <summary>Connection to the GIB e-Beyanname REST API (integrator credentials).</summary>
public sealed class GibOptions
{
    public const string Section = "Gib";

    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public string Channel { get; set; } = "ENTEGRATOR";
    public string Integrator { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>Taxpayer (MUKELLEF-VKN) used by the GIB connectivity check when the caller gives none.</summary>
    public string ProbeTaxpayerId { get; set; } = "";

    /// <summary>Service that echoes the caller's public IP, used by the connectivity check; empty disables it.</summary>
    public string PublicIpEchoUrl { get; set; } = "https://api.ipify.org";
}

public sealed class BridgeOptions
{
    public const string Section = "Bridge";

    public List<BridgeClient> Clients { get; set; } = [];

    /// <summary>Only GIB paths starting with one of these are forwarded.</summary>
    public List<string> AllowedPathPrefixes { get; set; } = [];

    /// <summary>Paths containing any of these are refused (e.g. "onayla" on a test bridge).</summary>
    public List<string> BlockedPathFragments { get; set; } = [];

    /// <summary>Also log request/response bodies. Bodies hold taxpayer data - keep off in production.</summary>
    public bool LogBodies { get; set; }
}

/// <summary>A customer IFS installation allowed to use the bridge.</summary>
public sealed class BridgeClient
{
    public string Id { get; set; } = "";

    /// <summary>Hex SHA-256 of the client's secret; the secret itself is never stored.</summary>
    public string SecretSha256 { get; set; } = "";

    /// <summary>VKN/TCKN values this client may file for; "*" allows any.</summary>
    public List<string> TaxpayerIds { get; set; } = [];

    public bool Enabled { get; set; } = true;
}
