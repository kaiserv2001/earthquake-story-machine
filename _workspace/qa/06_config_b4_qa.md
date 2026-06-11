# QA Pass 06 — Q.2 Boundary B4: config keys (local.settings ↔ Bicep ↔ code reads)

**Owner:** qa-engineer · **Trigger:** B.2 (Bicep) done
**Verdict: PASS** — all 6 app-specific config keys match exactly across all three sides; binding-expression
keys (`ServiceBusConnection`, `UsgsPollSchedule`) included and correct.
**Level:** Static (all three sides read & diffed by exact string). `az bicep build` deferred — `az`/`bicep`
CLI absent in this environment (belongs to infra-engineer B.6; not a B4 key-matching concern).

## Sides compared
- **A — local.settings.example.json:** `src/Quake.Functions/local.settings.example.json`
- **B — Bicep app settings:** `infra/main.bicep` `appSettings[]` (lines 203–255) + `infra/main.bicepparam`
- **C — code reads:** `Program.cs` `cfg[...]`, `UsgsPollerFunction.cs` `cfg[...]`, and trigger **binding
  expressions** (`Connection = "..."`, `%...%`) on the Function attributes.

## The B4 carry-over correction, verified
Pre-read flagged "8 keys, not 6 — includes binding-expression keys `ServiceBusConnection` and
`UsgsPollSchedule`." Confirmed and reconciled: there are **6 app-specific keys** (the two binding keys ARE
among them — they're read by binding attributes, not `cfg[...]`, which is exactly why they're easy to miss)
**plus 2 Functions-framework keys** (`AzureWebJobsStorage`, `FUNCTIONS_WORKER_RUNTIME`) that also appear in
local.settings = **8 entries total** in local.settings.json. Both framings agree; the table below is the
authoritative key-by-key diff.

## Key matrix — app-specific config (the B4 surface)
| Key | local.settings | Bicep appSettings | Code read (where / how) | Match |
|---|---|---|---|---|
| `ServiceBusConnection` | ✓ (line 6) | ✓ (233) | **binding** — `Connection="ServiceBusConnection"` on `ServiceBusTrigger` (StoryBuilderFunction.cs:22) **and** `ServiceBusOutput` (UsgsPollerFunction.cs:19) | ✓ |
| `BlobStorageConnection` | ✓ (7) | ✓ (237) | `cfg["BlobStorageConnection"]` (Program.cs:45) | ✓ |
| `SqlConnection` | ✓ (8) | ✓ (241) | `cfg["SqlConnection"]` (Program.cs:48) | ✓ |
| `UnsplashAccessKey` | ✓ (9) | ✓ (245) | `cfg["UnsplashAccessKey"]` (Program.cs:36) | ✓ |
| `UsgsMinMagnitude` | ✓ (10) | ✓ (249) | `cfg["UsgsMinMagnitude"]` (UsgsPollerFunction.cs:24) | ✓ |
| `UsgsPollSchedule` | ✓ (11) | ✓ (253) | **binding** — `[TimerTrigger("%UsgsPollSchedule%")]` (UsgsPollerFunction.cs:21) | ✓ |

Exhaustive reverse-check: grepped the **entire** `src/Quake.Functions` tree for `Configuration` / `cfg[` /
`GetEnvironmentVariable` / `%...%` / `Connection =`. No config key is read in code that is missing from Bicep,
and no app-specific key in local.settings/Bicep is unused. (The `OpenMeteoClient` `GetValueOrDefault` hit is a
WMO-code dictionary lookup, not config; `obj/` hits are build artifacts.)

## Framework / platform keys (present, correct — not part of the app B4 surface)
| Key | local.settings | Bicep | Note |
|---|---|---|---|
| `AzureWebJobsStorage` | ✓ `UseDevelopmentStorage=true` | ✓ (205) → `storageConnection` | required by host |
| `FUNCTIONS_WORKER_RUNTIME` | ✓ `dotnet-isolated` | ✓ (213) `dotnet-isolated` | isolated worker — matches |
| `FUNCTIONS_EXTENSION_VERSION` | (local n/a) | ✓ (209) `~4` | Azure-only; correct |
| `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` | (local n/a) | ✓ (217) | required for Consumption (Y1) plan |
| `WEBSITE_CONTENTSHARE` | (local n/a) | ✓ (221) | required for Consumption (Y1) plan |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | (local n/a) | ✓ (225) | matches `AddApplicationInsightsTelemetryWorkerService` in Program.cs:16 |

Local-only framework keys not needed in Azure: none missing. Azure-only keys above are correctly absent from
local.settings (they're platform infra, not app config). This split is correct, not a mismatch.

## Secrets handling (bicepparam) — spot-checked, correct
`main.bicepparam` sources `unsplashAccessKey` / `sqlAdminPassword` via `readEnvironmentVariable(...)` (GitHub
secrets `UNSPLASH_ACCESS_KEY`, `SQL_ADMIN_PASSWORD`), never literals — matches the contract that real keys come
at deploy time. Non-secret `usgsMinMagnitude='4.5'` and `usgsPollSchedule='0 */5 * * * *'` **mirror
local.settings exactly**.

## Defects
**None.** B4 holds across all three sides.

## Deferred / for infra-engineer (NOT a B4 defect)
- `az bicep build` clean is a Sprint-2 exit criterion but `az`/`bicep` CLI is unavailable here — assign to
  infra-engineer's B.6 validation (`_workspace/07_infra_validation.md`). Static structure of `appSettings[]`,
  params, and resource refs read clean this pass, but the compiler check is **deferred**, not passed.
