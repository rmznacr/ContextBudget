# Database design

PostgreSQL is the source of truth for projects and analysis history.

## Initial entities

- `Projects`: id (UUID), name, description, created/updated timestamps.
- `Traces`: id, project id, trace/correlation ids, provider/model, occurrence timestamp, status, latency, input/output/cached provider usage (nullable), created timestamp.
- `TraceSegments`: id, trace id, ordinal, role, content type, source id, tool-call id, token estimate (nullable), content hash, character count. Raw content is omitted by default.
- `Analyses`: id, project id, trace id (nullable), analyzer version, estimated input/output count, provider count snapshot (nullable), status and timestamp.
- `Findings`: id, analysis id, category/severity, explanation, affected segment ids, suggested action, opportunity (nullable), confidence, strategy.
- `OptimizationCandidates`: id, analysis id, strategy, before/after estimate, risk, diff metadata and acceptance state. Raw candidate text is not stored unless explicit retention is later enabled.
- `EvaluationDatasets`: project/name/created time; no raw case data.
- `EvaluationDatasetVersions`: immutable version number, case count, SHA-256 content fingerprint, evaluator version and creation time.
- `Experiments`: project and dataset-version foreign keys, name/provider/model/repeat/inference settings, baseline/optimized context fingerprints, status/timestamps and run-count/latest-gate summary fields derived from run records.
- `ExperimentRuns`: experiment id, idempotency key, status, quality gate, JSON aggregate summary, immutable JSON pricing snapshot and timestamps. Unique `(experiment_id, idempotency_key)`.
- `ExperimentCaseResults`: run/case/repeat/variant identity, status, nullable provider usage, local estimates, latency, JSON quality checks and safe failure category. Unique `(run_id, case_id, repeat, variant)`. Never store case input, expected output, candidate context, model response, or API key.
- `PricingProfiles`: provider/model/mode, currency, input/output/cached-input/cache-write prices per million tokens, effective date range, source description and creation time. Rate values are decimal; a new version may close an open interval once, without modifying its rates. Overlap validation runs transactionally.

Migrations implement Projects, trace/analysis history, experiment metadata/results, pricing profiles, and run pricing snapshots. Dataset content and candidate contexts are resupplied in memory when a run starts; fingerprints detect mismatch and preserve version lineage without allowing history to reconstruct raw content. Foreign keys cascade only for owned child rows; deleting a project removes its dependent history after explicit user action. Usage and price values are nullable: unknown is not zero. Historical calculations use the serialized rate snapshot captured when the run starts, even after a newer profile is added.

## Indexes and constraints

Index project-created time, trace correlation id, and provider/model-time. Use decimal numeric columns for prices and token totals as integers. Enforce unique analysis version/idempotency keys where applicable. EF Core migrations are authoritative.
