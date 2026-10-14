using ContextBudget.Application;
using ContextBudget.Domain;
using System.Text.Json;

namespace ContextBudget.Tests;

public sealed class ExperimentTests
{
    [Fact]
    public void DatasetFingerprintIsStableAndChangesWithContent()
    {
        var cases = new[] { new EvaluationCaseInput("1", "Case", "input", RequiredFacts: ["fact"]) };
        Assert.Equal(DatasetFingerprint.Of(cases), DatasetFingerprint.Of(cases));
        Assert.NotEqual(DatasetFingerprint.Of(cases), DatasetFingerprint.Of([cases[0] with { Input = "different" }]));
        Assert.Equal(DatasetFingerprint.Context("candidate"), DatasetFingerprint.Context("candidate"));
        Assert.NotEqual(DatasetFingerprint.Context("candidate"), DatasetFingerprint.Context("raw content changed"));
    }

    [Fact]
    public void DatasetValidatesDuplicateIdsAndBounds()
    {
        Assert.NotNull(DatasetFingerprint.ValidateCases([new("same", "A", "input"), new("same", "B", "input")]));
        Assert.Null(DatasetFingerprint.ValidateCases([new("id", "A", "input", Severity: "critical")]));
    }

    [Fact]
    public void DeterministicEvaluatorsCoverExactFactsKeywordsAndJsonSchema()
    {
        var evaluator = new DeterministicQualityEvaluator();
        var textCase = new EvaluationCaseInput("id", "case", "input", ExpectedOutput: "Hello  world", RequiredFacts: ["world"], RequiredKeywords: ["hello"]);
        Assert.All(evaluator.Evaluate(textCase, " Hello  world ", "context"), x => Assert.Equal("Passed", x.Result));
        var jsonCase = new EvaluationCaseInput("json", "schema", "input", JsonSchema: Schema("{\"type\":\"object\",\"required\":[\"ok\"],\"properties\":{\"ok\":{\"type\":\"boolean\"}}}"), ValidateJson: true);
        Assert.All(evaluator.Evaluate(jsonCase, "{\"ok\":true}", "context"), x => Assert.Equal("Passed", x.Result));
        Assert.Contains(evaluator.Evaluate(jsonCase, "{\"ok\":\"no\"}", "context"), x => x.Name == "JsonSchema" && x.Result == "Failed");
        var unsupportedSchema=evaluator.Evaluate(jsonCase with { JsonSchema=Schema("{\"type\":\"string\",\"minLength\":2}") }, "\"a\"", "context").Single(x=>x.Name=="JsonSchema");
        Assert.Equal("NotEvaluated",unsupportedSchema.Result);
        Assert.Contains(evaluator.Evaluate(jsonCase, "not json", "context"), x => x.Name == "JsonValidity" && x.Result == "Failed");
    }

    [Fact]
    public void QualityGateDistinguishesRegressionIncompleteManualAndPassed()
    {
        var passed = Check("RequiredFact", "Passed", "critical"); var failed = Check("RequiredFact", "Failed", "critical");
        var a = Result("A", [passed]); var b = Result("B", [failed]);
        Assert.Equal("REGRESSION_DETECTED", ExperimentSummarizer.Build([a, b], 2).QualityGate);
        Assert.Equal("EVALUATION_INCOMPLETE", ExperimentSummarizer.Build([a], 2).QualityGate);
        var manual = Result("B", [Check("ManualReview", "NotEvaluated", "normal")]);
        Assert.Equal("MANUAL_REVIEW_REQUIRED", ExperimentSummarizer.Build([Result("A", []), manual], 2).QualityGate);
        var noRules = Check("DefinedChecks", "NotEvaluated", "normal");
        Assert.Equal("NOT_EVALUATED", ExperimentSummarizer.Build([Result("A", [noRules]), Result("B", [noRules])], 2).QualityGate);
        Assert.Equal("PASSED_DEFINED_CHECKS", ExperimentSummarizer.Build([Result("A", [passed]), Result("B", [passed])], 2).QualityGate);
    }

    [Fact]
    public async Task RunnerPairsVariantsBalancesOrderAndPersistsOnlyMetricsAndChecks()
    {
        var store = new MemoryExperimentStore(); var provider = new FakeProvider();
        var runner = new ExperimentRunner(store, provider, new HeuristicTokenCounter(), new DeterministicQualityEvaluator());
        var cases = new[] { new EvaluationCaseInput("c1", "One", "question-1", RequiredKeywords: ["answer"]), new EvaluationCaseInput("c2", "Two", "question-2", RequiredFacts: ["answer"]) };
        await runner.Execute(new ExperimentJob(store.RunRecord.Id, store.ExperimentRecord.Id, "system A", "system B", cases), CancellationToken.None);
        Assert.Equal(8, provider.Requests.Count);
        Assert.Equal(["system A", "system B", "system B", "system A", "system B", "system A", "system A", "system B"], provider.Requests.Select(x => x.Messages[0].Content));
        Assert.Equal(8, store.Results.Count);
        Assert.Equal("Completed", store.RunRecord.Status);
        Assert.Contains("system A", provider.Requests.Select(x => x.Messages[0].Content));
        Assert.Contains("system B", provider.Requests.Select(x => x.Messages[0].Content));
        Assert.DoesNotContain("question", string.Join("|", store.Results.Select(x => x.CaseName + x.ChecksJson)));
        Assert.All(store.Results, x => Assert.Null(x.ProviderOutputTokens));
    }

    [Fact]
    public async Task RunnerStoresPartialFailuresAndHonorsCancellation()
    {
        var store = new MemoryExperimentStore(); var cases = new[] { new EvaluationCaseInput("c1", "One", "question") };
        await new ExperimentRunner(store, new FakeProvider(failOptimized: true), new HeuristicTokenCounter(), new DeterministicQualityEvaluator()).Execute(new(store.RunRecord.Id, store.ExperimentRecord.Id, "system A", "system B", cases), CancellationToken.None);
        Assert.Equal("PartiallyCompleted", store.RunRecord.Status);
        Assert.Contains(store.Results, x => x.FailureCode == "mock_failure");
        var cancelledStore = new MemoryExperimentStore(); using var cts = new CancellationTokenSource(30);
        await new ExperimentRunner(cancelledStore, new FakeProvider(block: true), new HeuristicTokenCounter(), new DeterministicQualityEvaluator()).Execute(new(cancelledStore.RunRecord.Id, cancelledStore.ExperimentRecord.Id, "system A", "system B", cases), cts.Token);
        Assert.Equal("Cancelled", cancelledStore.RunRecord.Status);
    }

    [Fact]
    public void MissingUsageStaysNullAndTokenReductionDoesNotDecideQualityGate()
    {
        var rows = new[] { Result("A", [Check("RequiredKeyword", "Passed", "normal")], 100), Result("B", [Check("RequiredKeyword", "Passed", "normal")], 10) };
        var summary = ExperimentSummarizer.Build(rows, 2);
        Assert.Equal(100, summary.BaselineInputTokens);
        Assert.Equal("PASSED_DEFINED_CHECKS", summary.QualityGate);
        Assert.Equal(90, summary.InputTokenDifference);
    }

    private static QualityCheck Check(string name, string result, string severity) => new(name, DeterministicQualityEvaluator.Version, result, result == "Passed" ? 1 : result == "Failed" ? 0 : null, result == "Failed" ? "failed" : "", severity);
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static ExperimentCaseResultDto Result(string variant, IReadOnlyList<QualityCheck> checks, long? inputTokens = null) => new("case", "case", 1, variant, "Succeeded", inputTokens, null, null, null, null, 10, checks, "");

    private sealed class FakeProvider(bool failOptimized = false, bool block = false) : ILLMProvider
    {
        public List<LLMRequest> Requests { get; } = [];
        public ProviderConfigurationDto GetConfiguration() => new("fake", "https://fake.invalid/v1", "fake-model", true, true);
        public async Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request); if (block) await Task.Delay(Timeout.Infinite, cancellationToken); var failed = failOptimized && request.Messages[0].Content.Contains('B'); return new LLMResponse("fake", "fake-model", failed ? null : "answer", failed ? null : new ProviderUsage(4, null, null), null, 2, failed ? "ERROR" : "SUCCESS", failed ? new ProviderError("mock_failure", "Mock failure") : null); }
        public Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderTestResult("fake", "fake-model", 0, null, "SUCCESS"));
    }

    private sealed class MemoryExperimentStore : IExperimentStore
    {
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Experiment ExperimentRecord { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "test", "fake", "fake-model", 2, "a", "b", null, null);
        public ExperimentRun RunRecord { get; } = new(Guid.NewGuid(), "key");
        public List<ExperimentCaseResult> Results { get; } = [];
        public MemoryExperimentStore() { RunRecord = new ExperimentRun(ExperimentRecord.Id, "key"); }
        public Task<bool> ProjectExists(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<EvaluationDataset?> Dataset(Guid id, CancellationToken ct) => Task.FromResult<EvaluationDataset?>(null);
        public Task<EvaluationDatasetVersion?> DatasetVersion(Guid id, CancellationToken ct) => Task.FromResult<EvaluationDatasetVersion?>(null);
        public Task AddDataset(EvaluationDataset dataset, EvaluationDatasetVersion version, CancellationToken ct) => Task.CompletedTask;
        public Task AddDatasetVersion(EvaluationDatasetVersion version, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<EvaluationDataset>> Datasets(Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<EvaluationDataset>>([]);
        public Task AddExperiment(Experiment experiment, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Experiment>> Experiments(Guid? projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Experiment>>([]);
        public Task<Experiment?> Experiment(Guid id, CancellationToken ct) => Task.FromResult<Experiment?>(ExperimentRecord);
        public Task<ExperimentRun?> Run(Guid id, CancellationToken ct) => Task.FromResult<ExperimentRun?>(RunRecord);
        public Task<ExperimentRun?> RunByKey(Guid experimentId, string key, CancellationToken ct) => Task.FromResult<ExperimentRun?>(null);
        public Task<ExperimentRun?> ActiveRun(Guid experimentId, CancellationToken ct) => Task.FromResult<ExperimentRun?>(null);
        public Task<IReadOnlyList<ExperimentRun>> Runs(Guid experimentId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExperimentRun>>([RunRecord]);
        public Task AddRun(ExperimentRun run, CancellationToken ct) => Task.CompletedTask;
        public Task SaveRun(ExperimentRun run, IReadOnlyList<ExperimentCaseResult> results, CancellationToken ct) { Results.AddRange(results); return Task.CompletedTask; }
        public Task SaveRunState(ExperimentRun run, Experiment experiment, string status, string gate, string summaryJson, CancellationToken ct) { run.Update(status, gate, summaryJson); experiment.SetStatus(status); return Task.CompletedTask; }
    }
}
