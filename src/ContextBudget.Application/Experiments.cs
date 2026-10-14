using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using ContextBudget.Domain;

namespace ContextBudget.Application;

public sealed record EvaluationCaseInput(string Id, string Name, string Input, string? ExpectedOutput = null, IReadOnlyList<string>? RequiredFacts = null, IReadOnlyList<string>? RequiredKeywords = null, JsonElement? JsonSchema = null, string Severity = "normal", IReadOnlyList<string>? RequiredInstructions = null, bool ManualReview = false, bool ValidateJson = false);
public sealed record DatasetCreateInput(Guid ProjectId, string Name, IReadOnlyList<EvaluationCaseInput> Cases);
public sealed record DatasetDto(Guid Id, Guid ProjectId, string Name, DateTimeOffset CreatedAtUtc, IReadOnlyList<DatasetVersionDto> Versions);
public sealed record DatasetVersionDto(Guid Id, int Version, int CaseCount, string ContentHash, string EvaluatorVersion, DateTimeOffset CreatedAtUtc);
public sealed record ExperimentCreateInput(Guid ProjectId, Guid DatasetVersionId, string Name, string Provider, string Model, int Repeats, string BaselineContext, string OptimizedContext, double? Temperature = null, int? MaxOutputTokens = null);
public sealed record ExperimentDto(Guid Id, Guid ProjectId, Guid DatasetVersionId, string Name, string Provider, string Model, int Repeats, string Status, DateTimeOffset CreatedAtUtc, string BaselineContextHash, string OptimizedContextHash, int TotalRunCount, int TotalExecutionCount, Guid? LatestRunId, string? LatestQualityGate);
public sealed record ExperimentStartInput(string IdempotencyKey, string BaselineContext, string OptimizedContext, IReadOnlyList<EvaluationCaseInput> Cases);
public sealed record QualityCheck(string Name, string Version, string Result, double? Score, string FailureReason, string Severity);
public sealed record ExperimentCaseResultDto(string CaseId, string CaseName, int Repeat, string Variant, string Status, long? ProviderInputTokens, long? ProviderOutputTokens, long? CachedTokens, long? EstimatedInputTokens, long? EstimatedOutputTokens, long LatencyMs, IReadOnlyList<QualityCheck> Checks, string FailureCode);
public sealed record ExperimentSummary(string QualityGate, int RequestedCalls, int CompletedCalls, int SuccessfulCalls, int FailedCalls, int PassedChecks, int FailedChecks, int NotEvaluatedChecks, int Regressions, long? BaselineInputTokens, long? OptimizedInputTokens, long? BaselineOutputTokens, long? OptimizedOutputTokens, double? InputTokenReductionPercent, long? InputTokenDifference, long? OutputTokenDifference, long? TotalTokenDifference, long? BaselineEstimatedInputTokens, long? OptimizedEstimatedInputTokens, long? BaselineEstimatedOutputTokens, long? OptimizedEstimatedOutputTokens, double? EstimatedInputTokenReductionPercent, long? EstimatedInputTokenDifference, long? EstimatedTotalTokenDifference, long? LatencyDifferenceMs, long? BaselineLatencyP50Ms, long? OptimizedLatencyP50Ms, int ManualReviewCases, string TokenMetric);
public sealed record ExperimentRunDto(Guid Id, Guid ExperimentId, string Status, string QualityGate, DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, ExperimentSummary Summary, IReadOnlyList<ExperimentCaseResultDto> Results);

public interface IExperimentStore
{
    Task<bool> ProjectExists(Guid projectId, CancellationToken ct);
    Task<EvaluationDataset?> Dataset(Guid id, CancellationToken ct);
    Task<EvaluationDatasetVersion?> DatasetVersion(Guid id, CancellationToken ct);
    Task AddDataset(EvaluationDataset dataset, EvaluationDatasetVersion version, CancellationToken ct);
    Task AddDatasetVersion(EvaluationDatasetVersion version, CancellationToken ct);
    Task<IReadOnlyList<EvaluationDataset>> Datasets(Guid? projectId, CancellationToken ct);
    Task AddExperiment(Experiment experiment, CancellationToken ct);
    Task<IReadOnlyList<Experiment>> Experiments(Guid? projectId, CancellationToken ct);
    Task<Experiment?> Experiment(Guid id, CancellationToken ct);
    Task<ExperimentRun?> Run(Guid id, CancellationToken ct);
    Task<ExperimentRun?> RunByKey(Guid experimentId, string key, CancellationToken ct);
    Task<ExperimentRun?> ActiveRun(Guid experimentId, CancellationToken ct);
    Task<IReadOnlyList<ExperimentRun>> Runs(Guid experimentId, CancellationToken ct);
    Task AddRun(ExperimentRun run, CancellationToken ct);
    Task<PricingProfile?> ActivePricing(string provider,string model,DateTimeOffset at,CancellationToken ct) => Task.FromResult<PricingProfile?>(null);
    Task SaveRun(ExperimentRun run, IReadOnlyList<ExperimentCaseResult> results, CancellationToken ct);
    Task SaveRunState(ExperimentRun run, Experiment experiment, string status, string gate, string summaryJson, CancellationToken ct);
}

public static class DatasetFingerprint
{
    public const string EvaluatorVersion = "deterministic-v1";
    public static string Of(IReadOnlyList<EvaluationCaseInput> cases) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cases))));
    public static string Context(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NormalizeExact(string value) => Regex.Replace(value.Trim().Replace("\r\n", "\n"), @"\s+", " ", RegexOptions.CultureInvariant);
    public static string? ValidateCases(IReadOnlyList<EvaluationCaseInput>? cases)
    {
        if (cases is null || cases.Count is < 1 or > 200) return "Dataset must contain 1–200 cases.";
        if (cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != cases.Count) return "Case IDs must be unique.";
        foreach (var c in cases) { if (string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 80 || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 160) return "Each case needs an ID (max 80) and name (max 160)."; if (string.IsNullOrWhiteSpace(c.Input) || c.Input.Length > 50_000) return "Case input is required and must be at most 50,000 characters."; if (c.ExpectedOutput?.Length > 50_000 || (c.JsonSchema is not null && c.JsonSchema.Value.GetRawText().Length > 20_000)) return "Case validation content is too long."; if ((c.RequiredFacts?.Count ?? 0) > 40 || (c.RequiredKeywords?.Count ?? 0) > 40 || (c.RequiredInstructions?.Count ?? 0) > 40) return "A case may define at most 40 checks of each type."; if ((c.RequiredFacts ?? []).Concat(c.RequiredKeywords ?? []).Concat(c.RequiredInstructions ?? []).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 500)) return "Evaluation check text must be 1–500 characters."; if (c.Severity is not ("critical" or "high" or "normal" or "low")) return "Severity must be critical, high, normal, or low."; if (c.JsonSchema is not null && c.JsonSchema.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False)) return "JSON Schema must be an object or boolean schema."; }
        return null;
    }
}

public sealed class DeterministicQualityEvaluator
{
    public const string Version = DatasetFingerprint.EvaluatorVersion;
    public IReadOnlyList<QualityCheck> Evaluate(EvaluationCaseInput c, string? response, string candidateContext)
    {
        var checks = new List<QualityCheck>();
        if (c.ExpectedOutput is not null) { var pass = response is not null && DatasetFingerprint.NormalizeExact(c.ExpectedOutput) == DatasetFingerprint.NormalizeExact(response); checks.Add(new("ExactMatch", Version, response is null ? "NotEvaluated" : pass ? "Passed" : "Failed", pass ? 1 : response is null ? null : 0, pass || response is null ? "" : "Expected output did not match after whitespace normalization.", c.Severity)); }
        foreach (var word in c.RequiredKeywords ?? []) checks.Add(ContainsCheck("RequiredKeyword", word, response, c.Severity));
        foreach (var fact in c.RequiredFacts ?? []) checks.Add(ContainsCheck("RequiredFact", fact, response, c.Severity));
        if (c.ValidateJson) checks.Add(JsonValidity(response, c.Severity));
        foreach (var instruction in c.RequiredInstructions ?? []) { var pass = candidateContext.Contains(instruction, StringComparison.Ordinal); checks.Add(new("InstructionPreservation", Version, pass ? "Passed" : "Failed", pass ? 1 : 0, pass ? "" : "Required instruction is absent from candidate context.", c.Severity)); }
        if (c.JsonSchema is not null) checks.Add(ValidateSchema(response, c.JsonSchema.Value.GetRawText(), c.Severity));
        if (c.ManualReview) checks.Add(new("ManualReview", Version, "NotEvaluated", null, "Manual review is required.", c.Severity));
        if (checks.Count == 0) checks.Add(new("DefinedChecks", Version, "NotEvaluated", null, "No deterministic checks were defined.", c.Severity));
        return checks;
    }
    private static QualityCheck ContainsCheck(string name, string value, string? response, string severity) { var pass = response?.Contains(value, StringComparison.OrdinalIgnoreCase) == true; return new(name, Version, response is null ? "NotEvaluated" : pass ? "Passed" : "Failed", response is null ? null : pass ? 1 : 0, response is null || pass ? "" : $"Required value not found ({name}).", severity); }
    private static QualityCheck JsonValidity(string? response, string severity) { if (response is null) return new("JsonValidity", Version, "NotEvaluated", null, "No model response.", severity); try { using var _ = JsonDocument.Parse(response); return new("JsonValidity", Version, "Passed", 1, "", severity); } catch (JsonException) { return new("JsonValidity", Version, "Failed", 0, "Response is not valid JSON.", severity); } }
    private static QualityCheck ValidateSchema(string? response, string schemaText, string severity)
    {
        if (response is null) return new("JsonSchema", Version, "NotEvaluated", null, "No model response.", severity);
        try { using var data = JsonDocument.Parse(response); using var schema = JsonDocument.Parse(schemaText); var root = schema.RootElement; ValidateNode(data.RootElement, root, "$", 0); return new("JsonSchema", Version, "Passed", 1, "", severity); }
        catch (UnsupportedSchemaException) { return new("JsonSchema", Version, "NotEvaluated", null, "Schema uses a keyword or form outside the supported deterministic subset.", severity); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { return new("JsonSchema", Version, "Failed", 0, "JSON validity/schema constraint failed.", severity); }
    }
    private static void ValidateNode(JsonElement data, JsonElement schema, string path, int depth)
    {
        if (depth > 32) throw new ArgumentException();
        if (schema.ValueKind == JsonValueKind.True) return; if (schema.ValueKind == JsonValueKind.False) throw new ArgumentException();
        if (schema.ValueKind != JsonValueKind.Object) throw new ArgumentException();
        var supported = new HashSet<string>(["type", "enum", "required", "properties", "items"], StringComparer.Ordinal);
        if (schema.EnumerateObject().Any(p => !supported.Contains(p.Name))) throw new UnsupportedSchemaException();
        if (schema.TryGetProperty("type", out var type)) { var ok = type.GetString() switch { "object" => data.ValueKind == JsonValueKind.Object, "array" => data.ValueKind == JsonValueKind.Array, "string" => data.ValueKind == JsonValueKind.String, "number" => data.ValueKind == JsonValueKind.Number, "integer" => data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out _), "boolean" => data.ValueKind is JsonValueKind.True or JsonValueKind.False, "null" => data.ValueKind == JsonValueKind.Null, _ => false }; if (!ok) throw new ArgumentException(); }
        if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(x => x.ToString() == data.ToString())) throw new ArgumentException();
        if (data.ValueKind == JsonValueKind.Object) { if (schema.TryGetProperty("required", out var required)) foreach (var p in required.EnumerateArray()) if (!data.TryGetProperty(p.GetString()!, out _)) throw new ArgumentException(); if (schema.TryGetProperty("properties", out var props)) foreach (var p in props.EnumerateObject()) if (data.TryGetProperty(p.Name, out var child)) ValidateNode(child, p.Value, path + "." + p.Name, depth + 1); }
        if (data.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items)) foreach (var child in data.EnumerateArray()) ValidateNode(child, items, path + "[]", depth + 1);
    }
    private sealed class UnsupportedSchemaException : Exception { }
}

public static class ExperimentSummarizer
{
    public static ExperimentSummary Build(IReadOnlyList<ExperimentCaseResultDto> rows, int requested, string requestedStatus = "Completed")
    {
        var complete = rows.Count; var successful = rows.Count(x => x.Status == "Succeeded"); var failed = complete - successful;
        var pairs = rows.GroupBy(x => (x.CaseId, x.Repeat)).Where(g => g.Any(x => x.Variant == "A") && g.Any(x => x.Variant == "B"));
        var regressions = pairs.Count(g => Checks(g, "A").Any(a => a.Result == "Passed" && Checks(g, "B").Any(b => b.Name == a.Name && b.Result == "Failed")));
        var all = rows.SelectMany(x => x.Checks).ToArray();
        var candidateCheckFailure = rows.Where(x => x.Variant == "B").SelectMany(x => x.Checks).Any(b => b.Result == "Failed");
        var manual = all.Any(c => c.Name == "ManualReview" && c.Result == "NotEvaluated");
        var evaluated = all.Where(c => c.Name is not ("ManualReview" or "DefinedChecks")).ToArray();
        var pendingChecks = all.Any(c => c.Name is not ("ManualReview" or "DefinedChecks") && c.Result == "NotEvaluated") || (evaluated.Length > 0 && all.Any(c => c.Name == "DefinedChecks" && c.Result == "NotEvaluated"));
        var pending = pendingChecks || complete < requested || failed > 0 || requestedStatus is "Running" or "Cancelled";
        var gate = candidateCheckFailure || regressions > 0 ? "REGRESSION_DETECTED" : pending ? "EVALUATION_INCOMPLETE" : manual ? "MANUAL_REVIEW_REQUIRED" : evaluated.Length == 0 ? "NOT_EVALUATED" : "PASSED_DEFINED_CHECKS";
        var ai = Sum("A", x => x.ProviderInputTokens); var bi = Sum("B", x => x.ProviderInputTokens); var ao = Sum("A", x => x.ProviderOutputTokens); var bo = Sum("B", x => x.ProviderOutputTokens);
        var aei=Sum("A",x=>x.EstimatedInputTokens);var bei=Sum("B",x=>x.EstimatedInputTokens);var aeo=Sum("A",x=>x.EstimatedOutputTokens);var beo=Sum("B",x=>x.EstimatedOutputTokens);
        var al = P50("A"); var bl = P50("B"); var inputDiff = ai is not null && bi is not null ? ai - bi : null; var outputDiff = ao is not null && bo is not null ? ao - bo : null; var estimatedInputDiff=aei is not null&&bei is not null?aei-bei:null;var estimatedOutputDiff=aeo is not null&&beo is not null?aeo-beo:null;
        return new(gate, requested, complete, successful, failed, all.Count(c => c.Result == "Passed"), all.Count(c => c.Result == "Failed"), all.Count(c => c.Result == "NotEvaluated"), regressions, ai, bi, ao, bo, ai is > 0 && bi is not null ? (ai.Value - bi.Value) * 100d / ai.Value : null, inputDiff, outputDiff, inputDiff is not null && outputDiff is not null ? inputDiff + outputDiff : null, aei, bei, aeo, beo, aei is > 0 && bei is not null ? (aei.Value-bei.Value)*100d/aei.Value:null, estimatedInputDiff, estimatedInputDiff is not null&&estimatedOutputDiff is not null?estimatedInputDiff+estimatedOutputDiff:null, al is not null && bl is not null ? al - bl : null, al, bl, rows.Where(x => x.Variant == "B" && x.Checks.Any(c => c.Name == "ManualReview")).Select(x => x.CaseId).Distinct().Count(), "Provider-reported input/output tokens; missing usage remains null. Estimates are separate local counts.");
        long? Sum(string variant, Func<ExperimentCaseResultDto, long?> value) { var selected = rows.Where(x => x.Variant == variant).ToArray(); return selected.Length > 0 && selected.All(x => value(x).HasValue) ? selected.Sum(x => value(x)!.Value) : null; }
        long? P50(string variant) { var vals = rows.Where(x => x.Variant == variant && x.Status == "Succeeded").Select(x => x.LatencyMs).Order().ToArray(); if (vals.Length < 5) return null; return vals[(int)Math.Ceiling(vals.Length * .5) - 1]; }
        static IEnumerable<QualityCheck> Checks(IEnumerable<ExperimentCaseResultDto> group, string variant) => group.First(x => x.Variant == variant).Checks;
    }
}

public interface IExperimentExecutionQueue { bool Enqueue(ExperimentJob job); bool Cancel(Guid runId); }
public sealed record ExperimentJob(Guid RunId, Guid ExperimentId, string BaselineContext, string OptimizedContext, IReadOnlyList<EvaluationCaseInput> Cases);

public sealed class ExperimentService(IExperimentStore store, IExperimentExecutionQueue queue, ILLMProvider provider)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> StartLocks = new();
    public async Task<IReadOnlyList<DatasetDto>> Datasets(Guid? projectId, CancellationToken ct) => (await store.Datasets(projectId, ct)).Select(ToDto).ToArray();
    public async Task<DatasetDto> CreateDataset(DatasetCreateInput input, CancellationToken ct)
    {
        var error = DatasetFingerprint.ValidateCases(input.Cases); if (error is not null) throw new ArgumentException(error); if (!await store.ProjectExists(input.ProjectId, ct)) throw new KeyNotFoundException("Project not found.");
        var dataset = new EvaluationDataset(input.ProjectId, input.Name); var version = new EvaluationDatasetVersion(dataset.Id, 1, input.Cases.Count, DatasetFingerprint.Of(input.Cases), DatasetFingerprint.EvaluatorVersion); dataset.Versions.Add(version); await store.AddDataset(dataset, version, ct); return ToDto(dataset);
    }
    public async Task<DatasetVersionDto> CreateDatasetVersion(Guid datasetId, IReadOnlyList<EvaluationCaseInput> cases, CancellationToken ct)
    {
        var error = DatasetFingerprint.ValidateCases(cases); if (error is not null) throw new ArgumentException(error);
        var dataset = await store.Dataset(datasetId, ct) ?? throw new KeyNotFoundException("Dataset not found."); var versionNumber = dataset.Versions.Count == 0 ? 1 : dataset.Versions.Max(x => x.Version) + 1;
        var version = new EvaluationDatasetVersion(datasetId, versionNumber, cases.Count, DatasetFingerprint.Of(cases), DatasetFingerprint.EvaluatorVersion); await store.AddDatasetVersion(version, ct); return new(version.Id, version.Version, version.CaseCount, version.ContentHash, version.EvaluatorVersion, version.CreatedAtUtc);
    }
    public async Task<IReadOnlyList<ExperimentDto>> Experiments(Guid? projectId, CancellationToken ct) => (await store.Experiments(projectId, ct)).Select(ToDto).ToArray();
    public async Task<ExperimentDto> CreateExperiment(ExperimentCreateInput input, CancellationToken ct)
    {
        var cfg = provider.GetConfiguration(); if (input.Provider != cfg.Provider || input.Model != cfg.Model || !cfg.Enabled) throw new ArgumentException("Experiment provider and model must match the currently enabled provider configuration.");
        if (input.Repeats is < 1 or > 3 || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.BaselineContext.Length > 250_000 || input.OptimizedContext.Length > 250_000 || input.Temperature is < 0 or > 2 || input.MaxOutputTokens is < 1 or > 32_000) throw new ArgumentException("Experiment settings are invalid.");
        if (!await store.ProjectExists(input.ProjectId, ct)) throw new KeyNotFoundException("Project not found."); var version = await store.DatasetVersion(input.DatasetVersionId, ct) ?? throw new KeyNotFoundException("Dataset version not found."); var dataset = await store.Dataset(version.DatasetId, ct); if (dataset?.ProjectId != input.ProjectId) throw new ArgumentException("Dataset version must belong to the selected project.");
        var experiment = new Experiment(input.ProjectId, input.DatasetVersionId, input.Name.Trim(), input.Provider, input.Model, input.Repeats, DatasetFingerprint.Context(input.BaselineContext), DatasetFingerprint.Context(input.OptimizedContext), input.Temperature, input.MaxOutputTokens); await store.AddExperiment(experiment, ct); return ToDto(await store.Experiment(experiment.Id, ct) ?? experiment);
    }
    public async Task<ExperimentDto?> GetExperiment(Guid id, CancellationToken ct) { var e=await store.Experiment(id,ct); return e is null?null:ToDto(e); }
    public async Task<ExperimentRunDto> Start(Guid id, ExperimentStartInput input, CancellationToken ct)
    {
        var gate = StartLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(ct);
        try { return await StartLocked(id, input, ct); } finally { gate.Release(); }
    }
    private async Task<ExperimentRunDto> StartLocked(Guid id, ExperimentStartInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length > 100) throw new ArgumentException("A valid idempotency key is required.");
        var existing = await store.RunByKey(id, input.IdempotencyKey, ct); if (existing is not null) return ToDto(existing);
        var active = await store.ActiveRun(id, ct); if (active is not null) return ToDto(active);
        var experiment = await store.Experiment(id, ct) ?? throw new KeyNotFoundException("Experiment not found."); var currentProvider = provider.GetConfiguration(); if (!currentProvider.Enabled || currentProvider.Provider != experiment.Provider || currentProvider.Model != experiment.Model) throw new ArgumentException("The experiment provider/model no longer matches the enabled server configuration."); var version = await store.DatasetVersion(experiment.DatasetVersionId, ct) ?? throw new KeyNotFoundException("Dataset version not found.");
        var error = DatasetFingerprint.ValidateCases(input.Cases); if (error is not null) throw new ArgumentException(error);
        if (input.Cases.Count != version.CaseCount || DatasetFingerprint.Of(input.Cases) != version.ContentHash || DatasetFingerprint.Context(input.BaselineContext) != experiment.BaselineContextHash || DatasetFingerprint.Context(input.OptimizedContext) != experiment.OptimizedContextHash) throw new ArgumentException("Start content does not match the immutable dataset version and experiment candidates.");
        if (input.Cases.Any(c => (c.RequiredFacts?.Count ?? 0) > 40 || (c.RequiredKeywords?.Count ?? 0) > 40 || (c.RequiredInstructions?.Count ?? 0) > 40)) throw new ArgumentException("A case has too many evaluation rules.");
        var run = new ExperimentRun(id, input.IdempotencyKey); var activePricing = await store.ActivePricing(experiment.Provider, experiment.Model, DateTimeOffset.UtcNow, ct); if (activePricing is not null) run.SetPricingSnapshot(JsonSerializer.Serialize(PricingService.Snapshot(activePricing), new JsonSerializerOptions(JsonSerializerDefaults.Web))); var initialSummary = ExperimentSummarizer.Build([], version.CaseCount * experiment.Repeats * 2); var initialJson=JsonSerializer.Serialize(initialSummary); run.Update("Pending", "EVALUATION_INCOMPLETE", initialJson); await store.AddRun(run, ct);
        if(!queue.Enqueue(new(run.Id,id,input.BaselineContext,input.OptimizedContext,input.Cases))){await store.SaveRunState(run,experiment,"Failed","EVALUATION_INCOMPLETE",initialJson,ct);throw new ArgumentException("Experiment queue is full; retry after a queued run finishes.");}
        return ToDto(run);
    }
    public async Task<IReadOnlyList<ExperimentRunDto>> Runs(Guid id, CancellationToken ct) => (await store.Runs(id, ct)).Select(ToDto).ToArray();
    public async Task<ExperimentRunDto?> GetRun(Guid runId, CancellationToken ct) { var r = await store.Run(runId, ct); return r is null ? null : ToDto(r); }
    public bool Cancel(Guid runId) => queue.Cancel(runId);
    private static DatasetDto ToDto(EvaluationDataset d) => new(d.Id, d.ProjectId, d.Name, d.CreatedAtUtc, d.Versions.OrderBy(x => x.Version).Select(v => new DatasetVersionDto(v.Id, v.Version, v.CaseCount, v.ContentHash, v.EvaluatorVersion, v.CreatedAtUtc)).ToArray());
    private static ExperimentDto ToDto(Experiment e) { var latest=e.Runs.OrderByDescending(x=>x.CreatedAtUtc).FirstOrDefault(); var actualCalls=e.Runs.Sum(x=>{try{using var doc=JsonDocument.Parse(x.SummaryJson);return doc.RootElement.TryGetProperty("CompletedCalls",out var n)?n.GetInt32():doc.RootElement.TryGetProperty("completedCalls",out n)?n.GetInt32():0;}catch(JsonException){return 0;}}); return new(e.Id,e.ProjectId,e.DatasetVersionId,e.Name,e.Provider,e.Model,e.Repeats,e.Status,e.CreatedAtUtc,e.BaselineContextHash,e.OptimizedContextHash,e.Runs.Count,actualCalls,latest?.Id,latest?.QualityGate); }
    private static ExperimentRunDto ToDto(ExperimentRun r)
    {
        var results = r.Results.Select(x => new ExperimentCaseResultDto(x.CaseId, x.CaseName, x.Repeat, x.Variant, x.Status, x.ProviderInputTokens, x.ProviderOutputTokens, x.CachedTokens, x.EstimatedInputTokens, x.EstimatedOutputTokens, x.LatencyMs, JsonSerializer.Deserialize<QualityCheck[]>(x.ChecksJson) ?? [], x.FailureCode)).ToArray();
        ExperimentSummary? summary = null;
        try { using var doc = JsonDocument.Parse(r.SummaryJson); if (doc.RootElement.TryGetProperty("qualityGate", out _)) summary = JsonSerializer.Deserialize<ExperimentSummary>(r.SummaryJson); } catch (JsonException) { }
        summary ??= ExperimentSummarizer.Build(results, r.Experiment?.DatasetVersion is null ? results.Length : r.Experiment.DatasetVersion.CaseCount * r.Experiment.Repeats * 2, r.Status);
        return new(r.Id, r.ExperimentId, r.Status, r.QualityGate, r.CreatedAtUtc, r.StartedAtUtc, r.CompletedAtUtc, summary, results);
    }
}

public sealed class ExperimentRunner(IExperimentStore store, ILLMProvider provider, ITokenCounter tokens, DeterministicQualityEvaluator evaluator)
{
    public async Task Execute(ExperimentJob job, CancellationToken ct)
    {
        var experiment = await store.Experiment(job.ExperimentId, ct); var run = await store.Run(job.RunId, ct); if (experiment is null || run is null) return;
        await store.SaveRunState(run, experiment, "Running", "EVALUATION_INCOMPLETE", "{}", ct); var rows = new List<ExperimentCaseResultDto>(); var entities = new List<ExperimentCaseResult>(); var count = 0; var requested = job.Cases.Count * experiment.Repeats * 2;
        try
        {
            for (var repeat = 0; repeat < experiment.Repeats; repeat++) foreach (var c in job.Cases)
            {
                var order = (repeat + Array.IndexOf(job.Cases.ToArray(), c)) % 2 == 0 ? new[] { "A", "B" } : new[] { "B", "A" };
                foreach (var variant in order)
                {
                    ct.ThrowIfCancellationRequested(); var context = variant == "A" ? job.BaselineContext : job.OptimizedContext; var started = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(90));
                        var response = await provider.CompleteAsync(new LLMRequest(experiment.Model, [new("system", context), new("user", c.Input)], experiment.Temperature, experiment.MaxOutputTokens), timeout.Token); started.Stop();
                        var ok = (response.Status.Equals("succeeded", StringComparison.OrdinalIgnoreCase) || response.Status.Equals("success", StringComparison.OrdinalIgnoreCase)) && response.Content is not null;
                        var checks = ok ? evaluator.Evaluate(c, response.Content, context) : Array.Empty<QualityCheck>(); var usage = response.Usage;
                        var row = new ExperimentCaseResultDto(c.Id, c.Name, repeat + 1, variant, ok ? "Succeeded" : "Failed", usage?.InputTokens, usage?.OutputTokens, usage?.CachedTokens, tokens.Count(experiment.Model, context + c.Input).Value, response.Content is null ? null : tokens.Count(experiment.Model, response.Content).Value, response.DurationMs > 0 ? response.DurationMs : started.ElapsedMilliseconds, checks, ok ? "" : response.Error?.Category ?? "provider_failure");
                        rows.Add(row); entities.Add(ToEntity(job.RunId, row)); count++;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { started.Stop(); var row = Failed(c, repeat, variant, started.ElapsedMilliseconds, tokens.Count(experiment.Model, context + c.Input).Value, "cancelled", "Cancelled"); rows.Add(row); entities.Add(ToEntity(job.RunId, row)); count++; throw; }
                    catch (OperationCanceledException) { started.Stop(); var row = Failed(c, repeat, variant, started.ElapsedMilliseconds, tokens.Count(experiment.Model, context + c.Input).Value, "timeout"); rows.Add(row); entities.Add(ToEntity(job.RunId, row)); count++; }
                    catch (Exception) when (!ct.IsCancellationRequested) { started.Stop(); var row = Failed(c, repeat, variant, started.ElapsedMilliseconds, tokens.Count(experiment.Model, context + c.Input).Value, "provider_failure"); rows.Add(row); entities.Add(ToEntity(job.RunId, row)); count++; }
                }
            }
            var finalStatus = rows.All(x => x.Status == "Succeeded") ? "Completed" : rows.Any(x => x.Status == "Succeeded") ? "PartiallyCompleted" : "Failed"; var summary = ExperimentSummarizer.Build(rows, requested, finalStatus);
            await store.SaveRun(run, entities, ct); await store.SaveRunState(run, experiment, finalStatus, summary.QualityGate, JsonSerializer.Serialize(summary), ct);
        }
        catch (OperationCanceledException)
        {
            var summary = ExperimentSummarizer.Build(rows, requested, "Cancelled"); await store.SaveRun(run, entities, CancellationToken.None); await store.SaveRunState(run, experiment, "Cancelled", "EVALUATION_INCOMPLETE", JsonSerializer.Serialize(summary), CancellationToken.None);
        }
    }
    private static ExperimentCaseResultDto Failed(EvaluationCaseInput c, int repeat, string variant, long latency, long estimatedInputTokens, string code, string status = "Failed") => new(c.Id, c.Name, repeat + 1, variant, status, null, null, null, estimatedInputTokens, null, latency, [], code);
    private static ExperimentCaseResult ToEntity(Guid runId, ExperimentCaseResultDto x) => new(runId, x.CaseId, x.CaseName, x.Repeat, x.Variant, x.Status, x.ProviderInputTokens, x.ProviderOutputTokens, x.CachedTokens, x.EstimatedInputTokens, x.EstimatedOutputTokens, x.LatencyMs, JsonSerializer.Serialize(x.Checks), x.FailureCode);
}
