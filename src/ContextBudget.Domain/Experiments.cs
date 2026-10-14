namespace ContextBudget.Domain;

public sealed class EvaluationDataset
{
    private EvaluationDataset() { }
    public EvaluationDataset(Guid projectId, string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new ArgumentException("Dataset name must be 1–120 characters."); Id = Guid.NewGuid(); ProjectId = projectId; Name = name.Trim(); CreatedAtUtc = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid ProjectId { get; private set; } public string Name { get; private set; } = ""; public DateTimeOffset CreatedAtUtc { get; private set; }
    public List<EvaluationDatasetVersion> Versions { get; private set; } = [];
}

public sealed class EvaluationDatasetVersion
{
    private EvaluationDatasetVersion() { }
    public EvaluationDatasetVersion(Guid datasetId, int version, int caseCount, string contentHash, string evaluatorVersion) { Id = Guid.NewGuid(); DatasetId = datasetId; Version = version; CaseCount = caseCount; ContentHash = contentHash; EvaluatorVersion = evaluatorVersion; CreatedAtUtc = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid DatasetId { get; private set; } public EvaluationDataset? Dataset { get; private set; } public int Version { get; private set; } public int CaseCount { get; private set; } public string ContentHash { get; private set; } = ""; public string EvaluatorVersion { get; private set; } = ""; public DateTimeOffset CreatedAtUtc { get; private set; }
}

public sealed class Experiment
{
    private Experiment() { }
    public Experiment(Guid projectId, Guid datasetVersionId, string name, string provider, string model, int repeats, string baselineHash, string optimizedHash, double? temperature, int? maxOutputTokens) { Id = Guid.NewGuid(); ProjectId = projectId; DatasetVersionId = datasetVersionId; Name = name; Provider = provider; Model = model; Repeats = repeats; BaselineContextHash = baselineHash; OptimizedContextHash = optimizedHash; Temperature = temperature; MaxOutputTokens = maxOutputTokens; Status = "Pending"; CreatedAtUtc = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid ProjectId { get; private set; } public Guid DatasetVersionId { get; private set; } public string Name { get; private set; } = ""; public string Provider { get; private set; } = ""; public string Model { get; private set; } = ""; public int Repeats { get; private set; } public string BaselineContextHash { get; private set; } = ""; public string OptimizedContextHash { get; private set; } = ""; public double? Temperature { get; private set; } public int? MaxOutputTokens { get; private set; } public string Status { get; private set; } = "Pending"; public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset? StartedAtUtc { get; private set; } public DateTimeOffset? CompletedAtUtc { get; private set; }
    public EvaluationDatasetVersion? DatasetVersion { get; private set; }
    public List<ExperimentRun> Runs { get; private set; } = [];
    public void SetStatus(string status) { Status = status; if (status == "Running") StartedAtUtc ??= DateTimeOffset.UtcNow; if (status is "Completed" or "PartiallyCompleted" or "Failed" or "Cancelled") CompletedAtUtc = DateTimeOffset.UtcNow; }
}

public sealed class ExperimentRun
{
    private ExperimentRun() { }
    public ExperimentRun(Guid experimentId, string idempotencyKey, string status = "Pending") { Id = Guid.NewGuid(); ExperimentId = experimentId; IdempotencyKey = idempotencyKey; Status = status; CreatedAtUtc = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid ExperimentId { get; private set; } public Experiment? Experiment { get; private set; } public string IdempotencyKey { get; private set; } = ""; public string Status { get; private set; } = "Pending"; public string QualityGate { get; private set; } = "NOT_EVALUATED"; public string SummaryJson { get; private set; } = "{}"; public string PricingSnapshotJson { get; private set; } = "null"; public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset? StartedAtUtc { get; private set; } public DateTimeOffset? CompletedAtUtc { get; private set; } public List<ExperimentCaseResult> Results { get; private set; } = [];
    public void SetPricingSnapshot(string snapshotJson) { if (Status != "Pending" || PricingSnapshotJson != "null") throw new InvalidOperationException("Pricing snapshot is immutable once set."); PricingSnapshotJson = snapshotJson; }
    public void Update(string status, string gate, string summary) { Status = status; QualityGate = gate; SummaryJson = summary; if (status == "Running") StartedAtUtc ??= DateTimeOffset.UtcNow; if (status is "Completed" or "PartiallyCompleted" or "Failed" or "Cancelled") CompletedAtUtc = DateTimeOffset.UtcNow; }
}

public sealed class ExperimentCaseResult
{
    private ExperimentCaseResult() { }
    public ExperimentCaseResult(Guid runId, string caseId, string caseName, int repeat, string variant, string status, long? providerInputTokens, long? providerOutputTokens, long? cachedTokens, long? estimatedInputTokens, long? estimatedOutputTokens, long latencyMs, string checksJson, string failureCode) { Id = Guid.NewGuid(); RunId = runId; CaseId = caseId; CaseName = caseName; Repeat = repeat; Variant = variant; Status = status; ProviderInputTokens = providerInputTokens; ProviderOutputTokens = providerOutputTokens; CachedTokens = cachedTokens; EstimatedInputTokens = estimatedInputTokens; EstimatedOutputTokens = estimatedOutputTokens; LatencyMs = latencyMs; ChecksJson = checksJson; FailureCode = failureCode; }
    public Guid Id { get; private set; } public Guid RunId { get; private set; } public ExperimentRun? Run { get; private set; } public string CaseId { get; private set; } = ""; public string CaseName { get; private set; } = ""; public int Repeat { get; private set; } public string Variant { get; private set; } = ""; public string Status { get; private set; } = ""; public long? ProviderInputTokens { get; private set; } public long? ProviderOutputTokens { get; private set; } public long? CachedTokens { get; private set; } public long? EstimatedInputTokens { get; private set; } public long? EstimatedOutputTokens { get; private set; } public long LatencyMs { get; private set; } public string ChecksJson { get; private set; } = "[]"; public string FailureCode { get; private set; } = "";
}
