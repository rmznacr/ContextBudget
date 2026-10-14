# API contracts

Base path: `/api/v1`. JSON uses camelCase. Errors use `application/problem+json` with a stable `code` extension and field-level validation details.

## Projects

- `GET /projects` → `Project[]`
- `POST /projects` `{name, description?}` → `201 Project`
- `GET /projects/{id}` → `Project` or `404`
- `PUT /projects/{id}` `{name, description?}` → `200 Project` or `404`
- `DELETE /projects/{id}` → `204` or `404`

## Traces and analysis

- `POST /projects/{projectId}/traces` accepts one trace document. Returns its trace id and token estimates. No raw message body is retained by default.
- `POST /projects/{projectId}/traces/import` accepts bounded `multipart/form-data` JSON file.
- `GET /projects/{projectId}/traces` returns paged metadata.
- `POST /projects/{projectId}/analyze` accepts `{trace, tokenBudget?, outputReserve?}`; persists trace metadata and returns categorized token totals, inspector findings and optimization candidates. Offline mode is the default.
- `POST /evaluation/offline` accepts original/candidate context, optional supplied responses and structural check settings; always returns `qualityVerified: false`.
- `GET /providers/config` returns provider name, server-owned BaseUrl/model, and only whether an API key is configured.
- `POST /providers/test` sends one short Chat Completions request using the configured model and returns status, safe error category, latency, and reported provider usage. It never returns the key or generated content. This can consume provider tokens and is called only after an explicit Settings action.
- `GET /projects/{projectId}/dashboard` returns measured aggregates; empty datasets have empty arrays/null totals, never synthetic KPIs.

## Datasets and experiments

- `GET /datasets?projectId={id}` lists dataset metadata and immutable versions. `POST /datasets` accepts `{projectId, name, cases:[...]}` and persists only the case count, content SHA-256, evaluator version, and dataset metadata.
- `POST /datasets/{id}/versions` accepts a case array and appends a new immutable version fingerprint and case count.
- `GET /datasets/{id}/versions` lists immutable version metadata.
- Case fields: `id`, `name`, `input`, optional `expectedOutput`, `requiredFacts`, `requiredKeywords`, `jsonSchema` (JSON object), `severity` (`critical|high|normal|low`), `requiredInstructions`, `validateJson`, and `manualReview`. Inputs are validated and retained only in request memory.
- `GET /experiments?projectId={id}` and `POST /experiments` list/create experiment metadata. Create accepts project, dataset-version id, name, provider/model, 1–3 repeats, baseline/optimized contexts and supported inference settings; contexts are fingerprinted and not persisted. Provider/model must match the current enabled server configuration.
- `GET /experiments/{id}` returns metadata; `GET /experiments/{id}/runs` returns recent run history.
- `POST /experiments/{id}/runs` requires `{idempotencyKey, baselineContext, optimizedContext, cases}`. Payload case/context fingerprints must match experiment and dataset metadata. The only invocation of the configured provider begins from this explicit endpoint/UI action. The response is `202` with a run representation; normal call count is `2 × caseCount × repeats`.
- `GET /experiments/runs/{runId}` supports status polling and returns per-variant metrics and checks. `POST /experiments/runs/{runId}/cancel` cancels queued or running local work.

The in-process queue executes one experiment at a time in one API instance and holds at most five waiting jobs, applies a per-call 90 second timeout, and does not replay tool calls. Pending jobs do not survive process termination and are not coordinated across multiple instances. Run status values: `Pending`, `Running`, `Completed`, `PartiallyCompleted`, `Failed`, `Cancelled`. Quality gates are `NOT_EVALUATED`, `EVALUATION_INCOMPLETE`, `PASSED_DEFINED_CHECKS`, `REGRESSION_DETECTED`, and `MANUAL_REVIEW_REQUIRED`; no `QUALITY_VERIFIED` value is emitted. The JSON Schema evaluator supports a deterministic MVP subset only (`type`, `required`, `properties`, `items`, `enum`).

## Pricing and analytics

- `GET /pricing-profiles?provider={provider}&model={model}` lists version rows; `GET /pricing-profiles/{id}` retrieves a version. `POST /pricing-profiles` adds a manual version with `{provider, modelId, currency, inputPricePerMillionTokens, outputPricePerMillionTokens, cachedInputPricePerMillionTokens?, cacheWritePricePerMillionTokens?, effectiveFromUtc, effectiveToUtc?, pricingMode, sourceDescription}`. Prices use decimal units per million tokens. A later version may close a prior open interval; other overlaps are rejected. Rate fields are immutable after creation. Only `cached-included-in-input` billing semantics are accepted until another provider billing mode is verified.
- `POST /cost-calculations` accepts `{provider, modelId, inputTokens?, outputTokens?, cachedInputTokens?, cacheWriteTokens?, atUtc?}` and calculates with the effective profile at the supplied/current UTC time. Its response includes calculation status, missing inputs, component breakdown and profile snapshot; it only accepts explicitly supplied provider usage and never prices local estimates.
- `GET /experiments/{id}/cost`, `/performance`, and `/comparison` summarize the most recent run; `/experiments/runs/{runId}/...` addresses one historical run. `GET /projects/{id}/analytics` returns run comparisons for a project.
- Cost summaries label calculated cost, currency, pricing profile id, A/B component totals, priced/unpriced call counts and quality gate. Historical runs use their saved pricing JSON snapshot. Unknown cached usage or missing cached price prevents a complete total. The API does not claim actual billed amounts or convert currencies.
- Performance summaries include successful/failed/timeout counts and rates, average/median/p95 latency, provider token usage and pair counts. Latency/token deltas require successful matching `(caseId, repeat)` A/B calls. p95 is null until at least 20 successful samples in the variant; cancelled calls are omitted from failure rate denominator. No time-to-first-token metric is returned.
- Project analytics include provider, model and run timestamp for UI filtering. Responses contain no API key, raw prompt, context or model response.

Unknown values are represented as `null` with a source/status field, not zero. Token counts include method/source and may be approximate. Offline checks do not certify answer quality.
