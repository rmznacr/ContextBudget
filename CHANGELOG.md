# Changelog

## [1.0.0] — 2026-10-14 (release candidate; not published)

### Added

- Local-first projects, trace ingestion, context inspection, token usage metadata, and deterministic analysis findings.
- Review-only context optimization candidates, including duplicate removal, retrieval/tool-output compaction, formatting cleanup, and context-budget allocation.
- Server-configured OpenAI-compatible provider adapter with explicit request initiation and restricted outbound connection policy.
- Dataset fingerprinting, paired A/B experiments with repeats, cancellation/idempotency handling, and deterministic quality checks.
- Versioned manual pricing, trace and experiment cost estimates, and latency/usage analytics grouped by currency.
- React dashboard and screens for context analysis, optimization, experiments, and cost/performance.
- PostgreSQL persistence and migrations, Docker Compose configuration, API contracts, and PostgreSQL-backed GitHub Actions workflow.

### Verified for this release candidate

- .NET Release build completed with 0 warnings and 0 errors.
- Backend tests against temporary PostgreSQL: 49 passed, 0 failed, 0 skipped.
- Frontend tests: 8 passed; frontend production build passed.
- Synthetic-data API persistence/restart and isolated Docker Compose smoke checks passed.
- GitHub Actions workflow has not been run remotely. Live EVREN connectivity and model availability are unverified; no live request was used.

### Known limitations

- No authentication or tenant isolation; local/private use only, not suitable for public exposure.
- Token estimates may be approximate and omit provider-specific chat framing.
- Optimization candidates and deterministic checks do not prove semantic quality; no judge or tool execution is included.
- Experiment execution uses a single-process, non-durable queue.
- Cost figures are estimates based on provider usage and manually maintained prices, not billing data. No FX, judge, or optimization-generation cost is measured.
- Raw prompts, dataset cases, contexts, and model replies are not retained in experiment history; source material must be resubmitted for a run.

This changelog entry prepares the v1.0.0 release. No Git tag or GitHub release has been created.
