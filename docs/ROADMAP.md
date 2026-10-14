# Roadmap

## Milestone 1 — Foundation and ingestion
- [x] Architecture and contributor docs
- [x] .NET modular solution and PostgreSQL migrations
- [x] Project CRUD, validated JSON/file import, token analyzer
- [x] Persist trace metadata and analysis history without raw content by default
- [x] React dashboard/projects/analyzer, real API states
- [x] Environment-gated PostgreSQL migration/CRUD integration test (runtime verified in Milestone 6 using a disposable PostgreSQL service)

## Milestone 2 — Inspection and optimization
- [x] Deterministic exact/normalized/retrieval/tool/budget findings
- [x] Review-only optimization candidates and side-by-side UI diff
- [x] Context-budget allocation, safe tool-output compaction and regression tests

## Milestone 3 — Secure provider integration
- [x] Provider-independent `ILLMProvider` and OpenAI-compatible adapter
- [x] Server-only EVREN configuration and explicit provider test action
- [x] HTTPS allowlist, DNS/socket pinning, private/reserved IP denial, no redirects/proxy, timeout and response limits
- [x] Fake HTTP, secret handling, SSRF and request-policy tests; no live EVREN call

## Milestone 4 — A/B experiments and quality evaluation
- [x] Dataset metadata and immutable, content-hash version records; no raw test data stored
- [x] PostgreSQL entities/configuration and EF migration for datasets, experiments, runs and case results
- [x] Fake provider paired execution, balanced order, repeats, timeout, cancellation, partial failures and idempotency protection
- [x] Deterministic exact/fact/keyword/JSON/schema/instruction/manual evaluators and explicit quality gates
- [x] Case-level and aggregate token/latency/evaluation results; usage remains null when provider omits it
- [x] Dataset/experiment/run API and explicit-start/poll/cancel frontend
- [x] Backend/frontend tests and docs; fake-provider execution verified in PostgreSQL integration coverage in Milestone 6

## Milestone 5 — Cost and performance analytics
- [x] Versioned manual pricing and effective-date overlap protection
- [x] Decimal component calculator with missing usage/pricing states
- [x] Historical profile snapshot per experiment run
- [x] Paired latency/token and failure/timeout analytics with sample-qualified p95
- [x] A/B calculated cost, quality gate context, project analytics and pricing UI
- [x] Backend/frontend automated tests and documentation; PostgreSQL runtime verified in Milestone 6 with a temporary test service

## Milestone 6 — V1 stabilization, integration and release
- [x] Trace pricing snapshots and project trace-cost analytics, separately categorized from experiment A/B runs
- [x] UI trace-cost states, N/A handling, metadata table, and valid-cost trend
- [x] PostgreSQL migration and isolated runtime verification, including fresh-context persistence check
- [x] Fake-provider A/B workflow, quality gate, pricing history, and project analytics exercised against PostgreSQL
- [x] Startup diagnostics, controlled migration policy, safe logging, and frontend request timeout feedback
- [x] PostgreSQL-backed CI workflow, README/changelog/release notes, and local install instructions
- [x] Release build, backend/frontend test/build, API restart, and isolated Compose smoke checks
- [ ] Trigger GitHub Actions on GitHub and confirm remote workflow result
- [ ] Optional live EVREN smoke test (requires configured credentials and explicit authorization)

## Later milestones
- [ ] Optional LLM-as-a-Judge
- [ ] Durable multi-instance job queue and recovery
- [ ] Broader API E2E and production deployment hardening
- [ ] Billing provider integration, measured optimization-generation costs and FX (only with explicit source/basis)
