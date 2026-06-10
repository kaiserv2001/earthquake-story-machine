# Sprint 2 — Frontend, Infra & Delivery

**Goal:** Browsable story-card frontend, deployable Bicep infra, reproducible local dev stack, CI/CD.
**Plan:** `plans/2026-06-10_163319-earthquake-story-machine.md` (Tasks 17–20)
**Team:** frontend-engineer, infra-engineer, qa-engineer (backend-engineer on-call for API contract fixes) · lead = quake-orchestrator
**Prerequisite:** Sprint 1 gate = GO; `_workspace/api-contract.md` published.
**Exit criteria (gate):** frontend renders live local pipeline data incl. empty/failure states · `az bicep build` + `docker compose config` clean · CI workflow green on a test push · `_workspace/qa/gate_sprint2.md` = GO

## Lane A — Frontend (frontend-engineer; read `frontend-spec` skill first)
| # | Plan task | Depends on | Deliverable | Status |
|---|---|---|---|---|
| A.1 | T17 List view (grid, badges, relative time) | contract | `frontend/index.html`, `app.js`, `styles.css` | ☐ |
| A.2 | T17 Detail view (hash route, hero photo + attribution, null-safe sections) | A.1 | detail render | ☐ |
| A.3 | T17 Empty + failure states; `staticwebapp.config.json` | A.1 | graceful zero-data UX | ☐ |
| A.4 | Check file | A.1–A.3 | `_workspace/06_frontend_check.md` | ☐ |

## Lane B — Infra (infra-engineer; read `infra-spec` skill first)
| # | Plan task | Depends on | Deliverable | Status |
|---|---|---|---|---|
| B.1 | T19 docker-compose local stack (replaces Sprint-1 stopgap) | — | azurite + mssql + SB emulator up | ☐ |
| B.2 | T18 Bicep templates | — | `infra/main.bicep` + params, `az bicep build` clean | ☐ |
| B.3 | T20 CI workflow | — | `.github/workflows/ci.yml` | ☐ |
| B.4 | T20 Deploy workflow | B.2 | `.github/workflows/deploy.yml` (secrets via GH) | ☐ |
| B.5 | T19 README quick-start | B.1 | clone→run instructions | ☐ |
| B.6 | Validation file | B.1–B.4 | `_workspace/07_infra_validation.md` | ☐ |

## QA (qa-engineer, incremental)
| # | Trigger | Checks | Status |
|---|---|---|---|
| Q.1 | A.2 done | B2: API response ↔ `app.js` field access (read both sides) | ☐ |
| Q.2 | B.2 done | B4: local.settings keys ↔ Bicep app settings ↔ Program.cs reads | ☐ |
| Q.3 | B.1 done | e2e on compose stack: full Sprint-1 proof re-run + frontend renders it | ☐ |
| Q.4 | all done | `_workspace/qa/gate_sprint2.md` go/no-go | ☐ |

## Notes
- Lanes A and B are fully independent — run in parallel from sprint start.
- Deployment to real Azure is **out of sprint scope**: templates and workflows are validated offline; actual `azd up`/first deploy happens only on explicit user request (requires Azure subscription + Unsplash key + GH secrets).
- Backlog (post-Sprint-2, user-approval items): map view of quakes, RSS/Atom feed of cards, Architecture Diagram artifact for the README, portfolio entry via `add-portfolio-project`.
