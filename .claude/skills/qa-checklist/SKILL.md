---
name: qa-checklist
description: Boundary-verification matrix and QA pass procedure for the Earthquake Story Machine. Read before every QA pass — after any module lands, when verifying integration, when investigating "data is null/missing" symptoms, and at sprint gates.
---

# QA Checklist — Boundary Verification

## Why boundaries
Single-module bugs get caught by the module's own tests. The bugs that survive to integration live where two artifacts must agree but are written by different agents at different times. Always verify by **reading both sides and comparing**, never by checking one side "looks right".

## Boundary matrix
| # | Side A | Side B | What must agree | Classic failure |
|---|---|---|---|---|
| B1 | `UsgsPollerFunction` serialize | `StoryBuilderFunction` deserialize | `JsonSerializerOptions` (casing!) | poller uses Web defaults (camelCase), builder uses default (PascalCase) → every field null, no exception |
| B2 | `StoryCardsApiFunction` response | `frontend/app.js` field access | property names & casing, null sections | JS reads `card.Quake.Magnitude`, API emits `quake.magnitude` |
| B3 | `StoryCard` record | blob JSON ↔ API `GetAsync` deserialize | same options both directions | card saved indented/Web, read back with mismatched options |
| B4 | `local.settings.json` keys | Bicep app settings + `Program.cs` `cfg[...]` reads | exact key strings | works locally, null connection string in Azure |
| B5 | EF entities + migration | actual SQL schema | column types, max lengths, unique `QuakeId` index | dedup silently broken if unique index missing |
| B6 | `Quake.Core` interfaces | five client implementations | signatures, null semantics | client throws where contract says return null |
| B7 | USGS feed fixture in tests | live feed | field presence (`mag` can be JSON null!) | parser crashes on real feed, passes on fixture |

## Pass procedure (per module-complete notification)
1. `dotnet build` + `dotnet test` — red stops the pass; file defect immediately.
2. Run the plan task's own **Verify** step.
3. Check every matrix row whose Side A *or* B changed.
4. Write `_workspace/qa/{NN}_{module}_qa.md`: verdict, rows checked, defects (owner, both files, one-line repro or failing assertion).
5. File each defect as a task assigned to the owning agent; SendMessage if it blocks others.

## Execution levels
Prefer the highest level available; degrade explicitly:
1. **Live** — run it (func start, curl, open frontend).
2. **Scripted** — `dotnet test`, smoke scripts.
3. **Static-only** — read both sides and diff shapes by hand; mark the report "static-only" so nobody mistakes it for a runtime pass.

## Sprint gate
At sprint end produce `_workspace/qa/gate_{sprint}.md`: all matrix rows status, open defects by severity, go/no-go recommendation. The e2e proof for Sprint 1 is: poller fires locally → message visible on queue → builder logs "Story card created" → `curl /api/cards` returns the card.
