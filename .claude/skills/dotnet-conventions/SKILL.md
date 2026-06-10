---
name: dotnet-conventions
description: Build, test, and coding conventions for the Earthquake Story Machine .NET 8 solution. Read before writing, building, or testing ANY C# code in this repo — covers project layout, isolated-worker Functions patterns, JSON serialization rules, commit format, and the exact build/test/run commands. Also applies when fixing build errors, adding packages, or wiring DI.
---

# .NET Conventions — Earthquake Story Machine

## Solution layout
```
EarthquakeStoryMachine.sln
src/Quake.Core/        # domain records, interfaces, assembler — NO Azure/HTTP deps
src/Quake.Data/        # EF Core only
src/Quake.Functions/   # isolated-worker host; all HTTP clients + functions
tests/Quake.Core.Tests/
```
Dependency rule: `Functions → Data → Core`, `Functions → Core`, tests → Core. Never reference Functions from Core/Data — Core stays pure so the assembler is unit-testable without Azure.

## Commands
```bash
dotnet build                                   # whole solution, run from repo root
dotnet test                                    # all tests
func start                                     # run Functions locally (cwd: src/Quake.Functions)
dotnet ef migrations add <Name> -p src/Quake.Data -s src/Quake.Functions
```
A task is complete only when `dotnet build` has 0 warnings-as-errors and `dotnet test` is green.

## Isolated worker patterns (Functions v4, .NET 8)
- Attributes: `[Function(nameof(X))]`, `[TimerTrigger("%UsgsPollSchedule%")]`, `[ServiceBusTrigger("quake-events", Connection = "ServiceBusConnection")]`, `[ServiceBusOutput(...)]`.
- Schedules and queue names that may vary by environment come from `%AppSetting%` tokens, not hardcoded cron strings.
- Constructor injection everywhere (primary constructors preferred); no static service access.
- `Program.cs` is the only composition root. New service ⇒ register there, same file.

## JSON rule (boundary-critical)
All serialization across boundaries (Service Bus messages, blob cards, HTTP API) uses the **same** options: `new JsonSerializerOptions(JsonSerializerDefaults.Web)`. The poller and the builder MUST share serializer settings — a mismatch produces silently-null fields, not errors. Centralize in `Quake.Core` as `QuakeJson.Options` if used in 3+ places.

## C# style
- Records for messages/DTOs (`sealed record`, `required init` properties); classes only for EF entities and services.
- Nullable enabled; a `?` on a StoryCard section means "enrichment may have failed" — preserve that semantics.
- File-scoped namespaces, one type per file, `Quake.{Project}.{Folder}` namespaces.

## Commits
One commit per plan task: `feat: ...`, `test: ...`, `fix: ...`, `infra: ...`. Plain author text only — never add Co-Authored-By or any AI attribution.
