# TrebynGibService

Bridge between customer IFS Cloud installations (TREBYN component) and the GIB
e-Beyanname REST API. IFS acts as the integrator: only this service, running on
the IFS server whose static IP GIB has authorized, talks to GIB. Customer IFS
installations call this service instead.

The bridge is deliberately thin. IFS builds every GIB request itself; the bridge
authenticates the customer, checks which taxpayer it may file for, adds the
integrator credentials and passes the call through unchanged. New GIB tabs or
fields need no change here.

## API

`POST /api/gib` with HTTP Basic credentials of a configured client:

```json
{
  "method": "DELETE",
  "path": "/kdv1/beyanname/toplu/kismiSil",
  "taxpayerId": "8600897228",
  "body": { "beyannameId": "467735", "sekmeAdi": "MATRAH" }
}
```

`path` is the GIB API path (with query string, if any). Once GIB has been reached
the answer is always HTTP 200, carrying GIB's own status and body:

```json
{ "status": 422, "body": { "messages": { "errorMessages": [ ... ] } } }
```

Bridge errors use other status codes: 401 unknown client, 403 taxpayer not allowed
for the client, 400 invalid or disabled request, 502 GIB not reachable.

`GET /health` returns `"OK"` without credentials (process is up).

`GET /health/gib[?taxpayerId=...]` (client credentials needed) makes one read-only
GIB call (`GET /kdv1/beyanname/versiyonlar`) with the integrator credentials and
returns 200 when GIB accepts it, 503 otherwise. Use it after installing on a new
server: `publicIp` is the address this server reaches the internet with - the one
GIB must have authorized - and `gibStatus` / `gibAnswer` show what GIB said.

```bash
curl -u ifs-dev:<secret> http://<host>:8080/health/gib
```

```json
{ "gibReachable": true, "gibStatus": 200, "elapsedMs": 412,
  "endpoint": "/kdv1/beyanname/versiyonlar", "gibAnswer": "{...}",
  "error": null, "publicIp": "203.0.113.10" }
```

## Configuration

| Setting | Meaning |
|---|---|
| `Gib:BaseUrl` | GIB API root, e.g. `https://ebeyanpreprodapi.gib.gov.tr` |
| `Gib:Token` | Integrator bearer token. **Secret** - never put it in appsettings files. |
| `Gib:Channel`, `Gib:Integrator` | `CHANNEL` / `ENTEGRATOR` headers sent to GIB |
| `Gib:ProbeTaxpayerId` | Taxpayer used by `/health/gib` when the call gives none (default: the client's first taxpayer) |
| `Gib:PublicIpEchoUrl` | Service `/health/gib` asks for this server's public IP (`https://api.ipify.org`); empty disables |
| `Bridge:Clients` | Customer installations: `Id`, `SecretSha256` (hex SHA-256 of the secret), `TaxpayerIds` (`"*"` = any), `Enabled` |
| `Bridge:AllowedPathPrefixes` | Only these GIB paths are forwarded |
| `Bridge:BlockedPathFragments` | Refused paths; `Development` blocks `onayla` so tests can never approve a declaration |
| `Bridge:LogBodies` | Log request/response bodies (taxpayer data - off in production) |

On the server, give secrets as environment variables (`Gib__Token`,
`Bridge__Clients__0__SecretSha256`, ...). Locally use user secrets:

```powershell
dotnet user-secrets set "Gib:Token" "<token>" --project src/TrebynGibService
dotnet user-secrets set "Bridge:Clients:0:Id" "ifs-dev" --project src/TrebynGibService
dotnet user-secrets set "Bridge:Clients:0:SecretSha256" "<hash>" --project src/TrebynGibService
dotnet user-secrets set "Bridge:Clients:0:TaxpayerIds:0" "8600897228" --project src/TrebynGibService
```

Hash of a client secret:

```powershell
$s = "<secret>"; ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($s)))).Replace("-", "")
```

## IFS side

IFS calls the bridge through the IFS Connect REST service `TREBYN_GIB_BRIDGE`
(see `TrebynGibService.plsql` in the TREBYN component). Its routing address points
to `https://<bridge-host>/api/gib` with Basic authentication using the client's
`Id` and secret.

## Docker (Linux server)

```bash
git clone https://github.com/halukazakli/TrebynGibService.git && cd TrebynGibService
cp .env.example .env        # fill in token and clients; .env is not committed
docker compose up -d --build
curl http://localhost:8080/health
curl -u <client-id>:<secret> http://localhost:8080/health/gib
```

The container listens on plain HTTP 8080 and runs as a non-root user. Put a
reverse proxy (nginx, Traefik...) with a TLS certificate in front of it - IFS
Connect must call the bridge over HTTPS. The image has a Docker HEALTHCHECK on
`/health`. Logs: `docker compose logs -f`.

## Build and test

```powershell
dotnet test
dotnet run --project src/TrebynGibService --launch-profile http
```

Requires the .NET 10 SDK.
