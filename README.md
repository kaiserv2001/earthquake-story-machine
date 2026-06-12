# Earthquake Story Machine

Event-driven C#/.NET 8 Azure pipeline that turns every significant earthquake from the
[USGS feed](https://earthquake.usgs.gov/earthquakes/feed/) into a rich, browsable "story
card" — enriched with reverse geocoding, a Wikipedia summary, current weather at the
epicentre, photos of the nearest city, and historical seismic context.

```
USGS feed ──(timer)──▶ Service Bus ──(trigger)──▶ enrichment Functions
                                                        │
                              ┌─────────────────────────┴───────────┐
                              ▼                                      ▼
                    Blob Storage (card JSON)              Azure SQL (metadata)
                              │                                      │
                              └──────────▶ HTTP API ◀────────────────┘
                                              │
                                              ▼
                                    Static Web App (frontend)
```

## Tech stack

.NET 8 (isolated worker) · Azure Functions v4 · Azure Service Bus · Azure Blob Storage ·
Azure SQL + EF Core 8 · Azure Static Web Apps (vanilla HTML/JS) · Bicep · xUnit.

## Repository layout

```
src/Quake.Core/        domain models, interfaces, assembler (pure, testable)
src/Quake.Data/        EF Core DbContext + entities
src/Quake.Functions/   Azure Functions host (poller, story builder, HTTP API)
frontend/              Static Web App
tests/                 xUnit tests
infra/                 Bicep templates + Service Bus emulator config
.github/workflows/     CI (build+test) and Deploy
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (`func`)
- [Docker](https://www.docker.com/) (for the local Azure dependencies)
- (optional) a free [Unsplash API access key](https://unsplash.com/developers) — without it,
  photo enrichment simply returns empty and the rest of the card still builds.

## Quick start (local)

```bash
# 1. Clone
git clone <repo-url> Earthquake && cd Earthquake

# 2. Start the local Azure dependencies (Azurite, SQL Server, Service Bus emulator)
cp .env.example .env          # dev-only SQL password; .env is gitignored
docker compose up -d --wait   # --wait blocks until every service is healthy
#    NOTE: the Service Bus emulator opens its port in ~1s but its AMQP gateway only
#    starts serving ~40s later. `--wait` gates on the `servicebus-ready` sidecar, which
#    probes the real AMQP handshake — do NOT start the Functions host before it returns,
#    or sends will fail with ConnectionRefused. Verify with:
docker compose ps

# 3. Configure the Functions host
cp src/Quake.Functions/local.settings.example.json src/Quake.Functions/local.settings.json
#    Edit local.settings.json: set ServiceBusConnection to the emulator string below,
#    and (optionally) paste your Unsplash key into UnsplashAccessKey.

# 4. Create the database schema
dotnet tool install --global dotnet-ef       # once
dotnet ef database update -p src/Quake.Data -s src/Quake.Functions

# 5. Run the Functions host (--cors lets the separately-served frontend call it)
cd src/Quake.Functions && func start --cors "*"

# 6. In another terminal, hit the API once a card has been built
curl http://localhost:7071/api/cards

# 7. Serve the frontend with any static server, e.g. `npx serve frontend`
#    When served from localhost, app.js calls the Functions host on :7071 directly
#    (see API_BASE in app.js). In the deployed Static Web App, /api/* is proxied to
#    the linked Functions backend instead, so no CORS or API_BASE override is needed.
```

### Local connection strings

| Setting | Local value |
|---|---|
| `BlobStorageConnection`, `AzureWebJobsStorage` | `UseDevelopmentStorage=true` (Azurite) |
| `SqlConnection` | `Server=localhost,1433;Database=QuakeDb;User Id=sa;Password=<your .env password>;TrustServerCertificate=true` |
| `ServiceBusConnection` | `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;` |

The `quake-events` queue is created automatically by the emulator from
`infra/servicebus-emulator/config.json`.

### Service Bus emulator fallback

The Service Bus emulator needs Docker and its own SQL Server companion (both are wired in
`docker-compose.yml`). If the emulator misbehaves on your machine, point
`ServiceBusConnection` at a real **Basic**-tier Service Bus namespace instead — at quake
volumes (a handful of messages per hour) this stays within the free grant. Create the
`quake-events` queue in that namespace and use its `RootManageSharedAccessKey` connection
string.

## Tests

```bash
dotnet test EarthquakeStoryMachine.sln
```

## Infrastructure & deployment

All Azure resources are described in `infra/main.bicep` (Service Bus Basic, Storage LRS,
Functions Y1 consumption, SQL serverless with auto-pause, Static Web App Free, Application
Insights). Every SKU is the cheapest that works, so an idle month costs ~$0.

Validate the template offline:

```bash
az bicep build --file infra/main.bicep      # no errors expected
```

**Deploying to Azure is opt-in** and not done automatically. The `deploy.yml` workflow runs
only when the `DEPLOY_ENABLED` repository variable is `true` and the required secrets
(`AZURE_CREDENTIALS`, `SQL_ADMIN_PASSWORD`, `UNSPLASH_ACCESS_KEY`,
`AZURE_STATIC_WEB_APPS_API_TOKEN`, …) are configured. To deploy manually:

```bash
az group create -n rg-quakestory -l <location>
UNSPLASH_ACCESS_KEY=<key> SQL_ADMIN_PASSWORD=<pw> \
  az deployment group create -g rg-quakestory -f infra/main.bicep -p infra/main.bicepparam
```

## CI/CD

- **`ci.yml`** — on every PR and push to `main`: restore, build (Release), and test on a
  pinned .NET 8 SDK.
- **`deploy.yml`** — on push to `main`: provisions infra via Bicep, publishes the Functions
  app, and deploys the frontend to the Static Web App. Gated behind `DEPLOY_ENABLED`;
  all credentials come from GitHub secrets.
