# Progress

## Current status

Milestones 1–6 implementation and local release verification are complete. V1 is READY based on local unit, PostgreSQL, API, and isolated Compose checks. The GitHub Actions workflow has not yet been run on GitHub. No live EVREN call was made.

## Milestone 6 delivered

- Independent traces now save only provider/model/time/usage and the applicable manual pricing snapshot. Project analytics return trace costs separately from experiment-run A/B totals, use N/A for missing usage or prices, group totals by currency, and limit the response to the latest 1,000 traces.
- Cost & Performance displays independent trace counts, cost states, usage metadata, and daily calculated-cost trends grouped by currency; experiment trends are also separated by currency.
- Added a schema migration for trace pricing snapshots, explicit development/Compose migration opt-in, pending-migration and database/provider configuration startup messages, timeout feedback, and sanitized API/queue exception logging.
- CI now uses a PostgreSQL 17 service and requires the gated integration test; frontend test/build stages remain included. Docker build contexts exclude generated output and installed dependencies.
- README, changelog, release scope, environment setup, startup behavior, retention policy, and local PostgreSQL/Compose instructions updated.

## Milestone 6 verification (2026-10-14)

- Release backend build: passed, 0 warnings and 0 errors.
- Backend tests with a temporary PostgreSQL service: 49 passed, 0 failed, 0 skipped. The integration test migrated and used an isolated random schema, exercised CRUD/trace analytics/pricing history, fake-provider A/B execution, quality checks, repeated idempotent starts and concurrent starts, then verified records through a fresh DbContext.
- Frontend tests: 8 passed. Frontend production build: passed.
- API smoke: project creation, analysis endpoint, pricing profile, trace snapshot and trace analytics passed with synthetic data. After API restart, the project and priced trace remained available.
- Isolated Compose clean-start: PostgreSQL health check, web static response, Nginx API proxy, and project create/read passed. Its containers and dedicated volume were removed.
- EF model/migration consistency: no pending model changes. `docker compose config --quiet`: passed.
- GitHub Actions workflow prepared but not triggered remotely. Live EVREN/provider connectivity remains optional and unverified; no key or live model request was used.

## Milestone 5 delivered

- Decimal prices per million tokens with provider/model/currency, effective dates, billing mode, source and optional cached/cache-write rates. No provider price is seeded or assumed.
- Manual profile list/detail/version and standalone cost calculation endpoints; cached-included-in-input is the only accepted billing mode until provider-specific alternatives are verified.
- New pricing versions preserve prior rates. When a later version is added, the open prior effective interval closes at its start time; overlapping intervals are rejected in a serializable transaction.
- Runs snapshot the profile effective when the run starts. Analytics use the snapshot, not current prices. Costs use provider-reported usage only, distinguish unavailable/incomplete from zero, expose component totals/missing call counts, and never label calculated cost as actual billed cost.
- A/B experiment and project analytics APIs, paired case/repeat latency/token comparisons, success/failure/timeout rates, average/median latency, and p95 requiring 20 successful observations per variant.
- Cost & Performance navigation with project/model/provider/date/run filters, real API trend bars, N/A explanations, and pricing profile create/list/version form. No savings projection, FX conversion, live billing, or real price data.
- API explicitly reports optimization generation/judge investment cost as unmeasured; quality gate remains visible beside price comparisons.
- Added calculator/performance unit tests, frontend empty/error tests, PostgreSQL pricing/snapshot assertions in the gated integration test, and EF migration `CostPerformanceAnalytics`.

## Milestone 4 delivered

- Immutable dataset version metadata stores case count, content hash, and evaluator version; raw cases remain in memory and must be resubmitted to run.
- Experiment metadata stores provider/model/settings and baseline/optimized context hashes. Run and case results store only status, nullable provider usage, local token estimates, latency, check JSON and safe failure categories.
- Explicit Start endpoint/UI action runs the same configured provider/model and test cases for A/B, 1–3 repeats, alternating variant order, 90-second per-call timeout, single-instance serialized queue (five waiting jobs max), idempotency and cancellation.
- Deterministic checks: whitespace-normalized exact match, required keyword/fact substring checks, JSON validity, JSON Schema MVP subset, required instruction-presence check, and manual-review marker.
- Quality gates distinguish incomplete data, defined-check pass, regression, manual review and no evaluated checks. Token reduction does not change the gate; no `QUALITY_VERIFIED` is emitted.
- Case-level and aggregate call/token/latency/check data; provider usage stays null when missing. p50 latency requires at least five successful observations for each variant. Cost/pricing and judge calls are excluded.
- Dataset/experiment/list/detail/start/poll/history/cancel APIs and dark-theme experiment creation, polling, history and side-by-side results UI.
- No raw prompts, dataset inputs, expected outputs, candidate contexts, model replies, tool outputs, or API keys are persisted by the experiment flow. Tool calls are not executed or replayed.

## Milestone 1–5 verification (historical; superseded by Milestone 6)

- `dotnet build ContextBudget.slnx --no-restore`: successful, 0 warnings/errors.
- `dotnet test ContextBudget.slnx --no-restore`: 48 passed, 0 failed, 1 PostgreSQL integration test skipped because `CONTEXTBUDGET_TEST_DB` is unset.
- `cd web && npm test`: 8 passed, 0 failed.
- `cd web && npm run build`: successful.
- EF migration `CostPerformanceAnalytics` adds pricing profiles and run snapshots. Docker daemon is unavailable. Local PostgreSQL is running and TCP port 5432 accepts connections, but `CONTEXTBUDGET_TEST_DB` is unset and the `psql` client is absent; no isolated runtime test could be configured safely.
- No live EVREN request or real API key was used.

## Milestone 1–5 limits (historical; later runtime verification is recorded above)

- The job queue is in-process and serialized for a single API instance. It is not durable, distributed, or recovered after process termination.
- JSON Schema evaluation supports `type`, `required`, `properties`, `items`, and `enum` only.
- Keyword and fact checks are substring matching, not semantic truth checks. `PASSED_DEFINED_CHECKS` covers only configured deterministic checks.
- No live provider behavior or PostgreSQL runtime integration has been verified.
- Cost analytics cover measured A/B inference only. No billing API, cache-write usage signal, FX, optimization generation API accounting, judge cost, or future break-even projection is implemented.
