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
| 2026-06-11 | Sprint 2 executed (incl. pause/resume mid-sprint) → gate GO, 0 defects; no harness changes needed. Resume pattern worked: persisted-state notes in the sprint doc were enough to respawn a lean 2-agent team (team/task dirs do NOT survive sessions — sprint-doc Notes are the durable state). Sprint-1 watch-item did not recur. Deferred to a Docker-enabled env: live e2e, live frontend render, CI runner-green (close-outs in gate file) | docs/sprints/SPRINT-02.md | sprint close-out |
| 2026-06-12 | SB-emulator residual closed: root cause was twofold (emulator startup race → infra sidecar fix; bundled SB SDK 7.17.x too old for emulator → backend bump to ext 5.24.0/SDK 7.20.1). Live SB transit verified 13/13, every pipeline hop now proven live. Harness lesson: a defect QA reproduces consistently can still be mis-attributed env-vs-code — the deciding evidence was a minimal standalone SDK probe at *two versions*; qa-checklist should prefer version-bisection before filing "environment" defects | docs/sprints/SPRINT-02.md | residual close-out |
