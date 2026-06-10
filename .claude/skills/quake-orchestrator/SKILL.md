---
name: quake-orchestrator
description: Orchestrates the Earthquake Story Machine build — creates the agent team (backend, enrichment, frontend, infra, QA), assigns sprint tasks, and coordinates execution. Use for ANY work request on this project; build/implement/continue the project, run/execute a sprint ("run sprint 1", "start sprint 2"), re-run or update or fix or improve parts ("redo the frontend", "fix the failing QA items", "update infra"), resume after interruption, or "where were we / what's left". Simple informational questions can be answered directly without this skill.
---

# Quake Orchestrator — Team Lead Playbook

You are the lead. Specialists do the building; you compose the team, route tasks, unblock, and gate quality.

**Execution mode: Agent team** (TeamCreate + TaskCreate + SendMessage). Sources of truth:
- Implementation plan: `plans/2026-06-10_163319-earthquake-story-machine.md` (task numbers, code, verify steps)
- Sprint docs: `docs/sprints/SPRINT-01.md`, `SPRINT-02.md` (who does what, in what order)
- Agent roster: `.claude/agents/` — backend-engineer, enrichment-engineer, frontend-engineer, infra-engineer, qa-engineer

## Phase 0 — Context check (always first)
Determine the execution mode before creating anything:
1. Read the sprint docs and `ls _workspace/ src/ frontend/ infra/` to assess progress.
2. Decide:
   - No `src/` and no `_workspace/` → **initial run**: start at Sprint 1, Wave 1.
   - Work exists + user asks for a specific part ("redo frontend", "fix QA defects") → **partial re-run**: spawn only the owning agent(s) + qa-engineer; skip the rest.
   - Work exists + user asks to continue → **resume**: `dotnet build && dotnet test` to find true state, then continue from the first incomplete sprint task.
   - Work exists + user changes direction materially → move `_workspace/` to `_workspace_prev/` and re-plan before building.
3. Report the chosen mode in one line before proceeding.

## Phase 1 — Team composition
Create team `quakestory` with only the agents the current wave needs (lean teams coordinate better):
- **Sprint 1, Wave 1:** backend-engineer + qa-engineer
- **Sprint 1, Wave 2:** + enrichment-engineer (after Core abstractions compile)
- **Sprint 2:** frontend-engineer + infra-engineer + qa-engineer (backend on-call for contract fixes)
Spawn every agent with `model: "opus"`. Each prompt: their agent file, the sprint doc section naming them, and the plan path.

## Phase 2 — Task assignment
Create tasks 1:1 with the sprint-doc rows, carrying the plan task number, owner, and dependencies (TaskCreate with explicit dependency links). Key dependencies are encoded in the sprint docs — do not flatten them; the waves exist because Core abstractions gate the enrichment lane and the API contract gates the frontend lane.

## Phase 3 — Coordination protocol
- **Tasks** carry state (pending → in_progress → completed); engineers self-update.
- **Files** carry artifacts: `_workspace/` for cross-agent handoffs (`api-contract.md`, `03_enrichment_smoke.md`, `qa/…`), code in the repo.
- **Messages** carry unblocking: backend → enrichment ("abstractions ready"), engineers → qa ("module done"), qa → owner (defects).
- Monitor; intervene only on stalls (>1 task cycle without progress), cross-agent disputes, or scope drift. Don't micro-manage working agents.

## Phase 4 — Quality gates
- qa-engineer runs incrementally per module (see `qa-checklist` skill); defects route to the owning agent as tasks.
- Sprint gate: qa-engineer's `_workspace/qa/gate_{sprint}.md` go/no-go before the next sprint starts.
- Sprint 1 exit proof (e2e): poller fires → Service Bus message → builder logs "Story card created" → `curl /api/cards` returns it.

## Error handling
- Agent fails a task: one retry with the failure context. Second failure → reassign or take over yourself; record the cause in the sprint doc's Notes column.
- Conflicting outputs (e.g., contract dispute): the owning agent decides (contract owner = backend); record the decision in `_workspace/api-contract.md` — never leave both versions live.
- External blockage (API down, no Docker): degrade per the owning agent's error-handling section; mark affected verify steps "deferred" in the sprint doc, don't silently pass them.
- Anything needing real Azure spend or credentials → stop and ask the user.

## Wrap-up (every run)
1. Update sprint doc statuses (single writer: only you edit sprint docs — engineers report, you record).
2. Summarize to the user: completed tasks, open defects, next wave.
3. Ask for feedback on results and team composition; route feedback per the harness evolution table (skill fix vs agent fix vs orchestrator fix) and log it in `CLAUDE.md` change history.
4. Disband the team (TeamDelete) when the sprint gate passes or work pauses.

## Test scenarios
- **Normal:** "build sprint 1" on empty repo → Phase 0 reports *initial run* → team of 2 (backend, qa) → Wave 1 tasks → backend publishes abstractions → enrichment-engineer joins → all Sprint-1 tasks green → qa gate file written → user summary.
- **Error:** enrichment-engineer reports Unsplash unreachable → client implemented against documented shape, smoke marked "unverified", qa records static-only check, gate lists it as a known gap — pipeline still passes e2e with photos section null.
- **Partial re-run:** "redo the frontend with bigger photos" with Sprint 2 done → Phase 0 reports *partial re-run* → team = frontend-engineer + qa-engineer only → B2 boundary re-checked → sprint doc updated.
