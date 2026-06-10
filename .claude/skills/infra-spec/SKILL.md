---
name: infra-spec
description: Azure resource specs, naming, SKUs, app settings, and CI/CD rules for the Earthquake Story Machine. Read before writing or modifying anything in infra/ (Bicep), docker-compose.yml, or .github/workflows/, and when adding any configuration key the Functions app reads.
---

# Infra Spec — Earthquake Story Machine

## Cost rule
Idle month ≈ $0. Every SKU below is chosen for that; upgrading any SKU requires explicit user approval.

## Resources (Bicep, single `infra/main.bicep`)
| Resource | Name pattern | SKU / config |
|---|---|---|
| Resource group | `rg-quakestory` | (created by deploy cmd) |
| Service Bus namespace | `sb-quakestory-{uniqueString}` | **Basic** (queues only) |
| └ Queue | `quake-events` | maxDeliveryCount 5, lockDuration PT5M |
| Storage account | `stquakestory{uniqueString}` | Standard_LRS; container `story-cards` |
| Function App | `func-quakestory-{uniqueString}` | Y1 consumption, `dotnet-isolated`, net8.0 |
| SQL server + DB | `sql-quakestory-{uniqueString}` / `QuakeDb` | GP_S_Gen5_1 serverless, auto-pause 60 min (or Free offer) |
| Static Web App | `swa-quakestory` | Free |
| App Insights | `appi-quakestory` | workspace-based |

`{uniqueString}` = `uniqueString(resourceGroup().id)`. Outputs: function app name, SWA deploy token reference, SQL FQDN.

## App settings contract (must match code exactly)
The Function App's app settings mirror `src/Quake.Functions/local.settings.json` `Values` keys 1:1:
`FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`, `ServiceBusConnection`, `BlobStorageConnection`, `SqlConnection`, `UnsplashAccessKey`, `UsgsMinMagnitude`, `UsgsPollSchedule`.
A key renamed in code but not here fails **only in production** — when backend-engineer announces a new config key, mirroring it in Bicep + workflow secrets is a blocking task, not a cleanup item.
Secrets (`UnsplashAccessKey`, SQL password) enter via `@secure()` params / GitHub secrets — never literals in the repo.

## Local dev (`docker-compose.yml`)
- `azurite` (mcr.microsoft.com/azure-storage/azurite) — blob :10000, queue :10001, table :10002
- `mssql` (mcr.microsoft.com/mssql/server:2022-latest) — :1433, `ACCEPT_EULA=Y`, dev-only password via `.env`
- `servicebus-emulator` (mcr.microsoft.com/azure-messaging/servicebus-emulator) + its required mssql dependency; queue `quake-events` declared in `config.json` mount
- Fallback documented in README: if the emulator misbehaves, point `ServiceBusConnection` at a real Basic namespace (~free at this volume).

## GitHub Actions
- `ci.yml` — on PR + push to main: `dotnet build --configuration Release` + `dotnet test`; .NET 8 via `actions/setup-dotnet`.
- `deploy.yml` — on push to main: build → `Azure/functions-action` for the Functions app; `Azure/static-web-apps-deploy` for `frontend/`. Credentials: `AZURE_CREDENTIALS` (OIDC preferred), `AZURE_STATIC_WEB_APPS_API_TOKEN`.
- Workflows must pass offline validation (YAML parse / actionlint) before commit.

## Validation (all offline — never deploy without explicit user request)
```bash
az bicep build --file infra/main.bicep      # must emit no errors
docker compose config -q                     # compose syntax
```
Record results in `_workspace/07_infra_validation.md`.
