# Sprint 1 — Core Pipeline (skeleton → working e2e backend)

**Goal:** A locally runnable pipeline: timer poller → Service Bus → story builder → Blob + SQL → HTTP API returns cards.
**Plan:** `plans/2026-06-10_163319-earthquake-story-machine.md` (task numbers below refer to it)
**Team:** backend-engineer, enrichment-engineer, qa-engineer · lead = quake-orchestrator
**Exit criteria (gate):** `dotnet test` green · e2e proof: poller fires → queue message → builder logs "Story card created" → `curl http://localhost:7071/api/cards` returns the card · QA gate file `_workspace/qa/gate_sprint1.md` = GO

## Wave 1 — Foundation (sequential, backend-engineer)
| # | Plan task | Owner | Depends on | Deliverable | Status |
|---|---|---|---|---|---|
| 1.1 | T1 Solution skeleton | backend-engineer | — | compiling sln, 4 projects | ☑ |
| 1.2 | T2 Domain models | backend-engineer | 1.1 | QuakeEvent, StoryCard records | ☑ |
| 1.3 | T3 Enrichment interfaces | backend-engineer | 1.2 | Abstractions compile → **announce to enrichment-engineer** | ☑ |
| 1.4 | QA pass: skeleton | qa-engineer | 1.3 | `qa/01_skeleton_qa.md` | ☑ |

## Wave 2 — Parallel lanes (starts when 1.3 lands)
**Lane A (backend-engineer):**
| # | Plan task | Depends on | Deliverable | Status |
|---|---|---|---|---|
| 2.1 | T4 StoryCardAssembler (TDD) | 1.3 | assembler + 3 tests green | ☑ |
| 2.2 | T5 UsgsFeedParser (TDD) | 1.2 | parser + fixture tests (incl. null `mag`) | ☑ |
| 2.3 | T11 EF Core DbContext + migration | 1.2 | Initial migration, unique QuakeId index | ☑ |
| 2.4 | T12 BlobStoryCardStore | 1.3 | store impl | ☑ |

**Lane B (enrichment-engineer)** — read `enrichment-client-pattern` skill first; one commit per client:
| # | Plan task | Depends on | Deliverable | Status |
|---|---|---|---|---|
| 2.5 | T6 NominatimClient | 1.3 | client + smoke entry | ☑ |
| 2.6 | T7 WikipediaClient | 1.3 | client + smoke entry | ☑ |
| 2.7 | T8 OpenMeteoClient | 1.3 | client + smoke entry | ☑ |
| 2.8 | T9 UnsplashClient | 1.3 | client + smoke entry (key needed — ask user) | ☑ |
| 2.9 | T10 UsgsHistoryClient | 1.3 | client + smoke entry → `_workspace/03_enrichment_smoke.md` complete | ☑ |

**QA (incremental, qa-engineer):**
| # | Trigger | Checks | Status |
|---|---|---|---|
| 2.10 | Lane A 2.1–2.2 done | tests run red→green honestly; B7 fixture-vs-live feed | ☑ |
| 2.11 | Lane B done | B6 interface/null semantics across all 5 clients | ☑ |
| 2.12 | 2.3 done | B5 migration has unique QuakeId index | ☑ |

## Wave 3 — Functions assembly (backend-engineer; needs both lanes)
| # | Plan task | Depends on | Deliverable | Status |
|---|---|---|---|---|
| 3.1 | T13 Program.cs DI + host/local.settings | 2.1–2.9 | host boots (`func start`) | ☐ |
| 3.2 | T14 UsgsPollerFunction | 3.1, 2.2, 2.3 | poller publishes fresh quakes | ☐ |
| 3.3 | T15 StoryBuilderFunction | 3.1, 2.1, 2.4 | builder writes Blob + SQL | ☐ |
| 3.4 | T16 StoryCardsApiFunction | 3.1 | API + **publish `_workspace/api-contract.md`** | ☐ |
| 3.5 | QA pass: B1 (serializer match), B3, B4 + e2e proof | 3.2–3.4 | `qa/04_pipeline_qa.md` + `qa/gate_sprint1.md` | ☐ |

## Notes
- **Deviation (2026-06-10, backend):** local env has only the .NET 10 SDK/runtime. Projects target net8.0 (ref packs) with root `Directory.Build.props` setting `RollForward=Major` so binaries execute locally on .NET 10. Azure deployment is unaffected (Functions host supplies net8); Sprint 2 infra must NOT carry RollForward assumptions into Bicep/CI. Classic `.sln` format used (not .slnx) for EF/CI tooling compatibility.
- **Plan fix (2026-06-10, lead):** `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` added to the Functions csproj in the plan (required by `ConfigureFunctionsWebApplication()`); commit 105cdc3.
- **Approved deviation (2026-06-10, lead/QA):** Service Bus message serialization uses shared `QuakeJson.Options` (Web defaults) in Quake.Core on BOTH poller and builder sides, superseding the plan's bare `JsonSerializer` calls (B1 prevention).
- **Deviation (2026-06-10, backend, #5):** plan's assembler photos call hit nullability warning-as-error; fixed with explicit `Safe<IReadOnlyList<PhotoInfo>>` wrapper, behavior unchanged.
- **Deviation (2026-06-10, backend, #7):** `dotnet-ef` tools v10.0.8 (only SDK available) against EF Core 8 packages; Initial migration generated correctly. `DesignTimeQuakeDbContextFactory` added in Quake.Data per plan's sanctioned fallback.
- Local infra for Wave 3 (Azurite/SQL/SB emulator): if Sprint 2's docker-compose isn't ready, backend-engineer may write a minimal throwaway compose file; infra-engineer replaces it in Sprint 2.
- Unsplash access key and SQL dev password come from the user — request once, store in `local.settings.json` (gitignored) only.
- Single writer rule: only the orchestrator edits this file (status ☐ → ☑, notes).
