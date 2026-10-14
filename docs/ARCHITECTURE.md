# Architecture

ContextBudget is a local-first modular monolith. The initial implementation uses four .NET projects: Domain (entities and rules), Application (use cases and ports), Infrastructure (EF Core/PostgreSQL and adapters), and API (HTTP contracts/composition). The React/Vite application is a separate client and talks to the API over versioned JSON endpoints.

## Boundaries and flow

`HTTP request -> validated API DTO -> Application use case -> domain analysis -> Infrastructure repositories -> PostgreSQL`

Trace import validates the document, classifies message/retrieval/tool segments, and computes local token estimates. If provider usage is present, it is kept separately from estimates. Raw prompt text is not persisted by default: the initial schema stores segment metadata and a content hash only. Offline analysis can use request memory and return findings/candidate diffs without recording raw text.

## Modules

- Projects: PostgreSQL-backed CRUD.
- Trace ingestion: JSON API and file upload with bounded request size and per-record validation.
- Token analysis: `ITokenCounter` dispatches through `IModelTokenizer` by model, then uses the Unicode-aware heuristic fallback. Infrastructure provides an OpenAI tiktoken adapter that reads a configured local vocabulary file; no vocabulary path is configured by default, so current values are `ESTIMATED`. Counts cover text content and omit provider-specific chat framing. Provider usage is separately retained when supplied. `IModelContextRegistry` holds context-window defaults used for candidate budgeting.
- Inspector and optimization: deterministic strategies over immutable input; risky suggestions are candidates requiring user acceptance. Budget allocation protects system/developer instructions and the latest user message, then uses explicit priority and recency to choose additional segments. This remains a review-only candidate and does not mutate a trace.
- Evaluation/providers: `ILLMProvider` is provider-independent. The OpenAI-compatible adapter sends Chat Completions requests through a server-configured HTTPS origin allowlist. Its connection policy validates public DNS answers before requests and in a socket-pinning connect callback; redirects and proxy use are disabled. The API key remains server-side. Provider tests are explicit user actions and no request is sent on application startup.
- Experiments: dataset and candidate contents are submitted on create/start and fingerprinted; only metadata, per-variant usage/latency, safe status codes, and deterministic checks persist. A single-instance in-process channel serializes runs; the runner pairs each A/B sample, alternates call order, supports 1–3 repeats, cancellation and a 90-second call timeout. It invokes the configured `ILLMProvider` only after explicit Start and never dispatches tools. Deterministic checks include normalized exact match, required substrings, JSON validity/schema subset, instruction-presence rules, and manual-review markers. A quality gate describes only the configured test coverage.
- Cost and performance analytics: `CostCalculator` is a pure Application service over provider-reported usage and a dated decimal pricing snapshot. The API/persistence adapters select a profile at experiment run start and store that immutable rate snapshot on the run. Analytics compare A/B costs and matched case/repeat performance; local token estimates never become cost usage. p95 requires 20 successful observations per variant. Billing, foreign-exchange conversion, judge cost and optimization-generation cost are not claimed.

## Security

DTO validation, size limits, safe JSON parsing, strict CORS, sanitized errors, masked secrets, and parameterized persistence are required. Provider destinations are configured and allowlisted; loopback/private/link-local/reserved IPv4/IPv6 targets are rejected during DNS validation and socket connection. Redirects and proxy use are disabled, TLS validation remains enabled, and requests are bounded by timeout, response size, and concurrency. Imported text is data, never executable instructions. Provider calls are explicit and cancellable.

## Runtime

Development uses ASP.NET Core 10, PostgreSQL, and Vite. Docker Compose is optional. OpenAPI describes the API. The experiment queue is process-local: it is not durable or multi-instance coordinated, and pending work must be resubmitted after process loss.
