# Earthquake Story Machine

Event-driven C#/.NET 8 Azure portfolio project: USGS quakes → Service Bus → enrichment Functions → story cards (Blob + SQL) → Static Web App.

## Harness: Earthquake Story Machine

**Goal:** Build and evolve the quake story-card pipeline with a specialist agent team executing the sprint docs.

**Trigger:** For any build/implementation/sprint/fix/continue request on this project, use the `quake-orchestrator` skill. Simple informational questions can be answered directly.

**Key paths:** plan `plans/2026-06-10_163319-earthquake-story-machine.md` · sprints `docs/sprints/` · handoffs `_workspace/`

**Change history:**
| Date | Change | Target | Reason |
|------|--------|--------|--------|
| 2026-06-10 | Initial harness: 5 agents (backend, enrichment, frontend, infra, qa), 6 skills, orchestrator | all | — |
| 2026-06-11 | Sprint 1 executed → gate GO, 0 defects; no harness changes needed. Watch-item: enrichment-engineer needed two nudges to start (possible spawn-prompt issue if it recurs in Sprint 2) | docs/sprints/SPRINT-01.md | sprint close-out |
