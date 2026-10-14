using ContextBudget.Application;
using ContextBudget.Domain;
using ContextBudget.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ContextBudget.Tests;

public sealed class FoundationTests
{
    [Fact]
    public void UnsupportedModelReturnsExplicitEstimatedCount()
    {
        var result=new HeuristicTokenCounter().Count("made-up-model","Türkçe karakterler ve Unicode 🙂");
        Assert.True(result.Value>0);
        Assert.Equal("ESTIMATED",result.Source);
    }

    [Fact]
    public void EmptyTextHasZeroEstimatedTokens()
    {
        var result=new HeuristicTokenCounter().Count("unknown","");
        Assert.Equal(0,result.Value);
        Assert.Equal("ESTIMATED",result.Source);
    }

    [Fact]
    public void ProjectRejectsBlankAndOversizedNames()
    {
        Assert.Throws<ArgumentException>(()=>new Project(" ",null));
        Assert.Throws<ArgumentException>(()=>new Project(new string('x',121),null));
    }

    [Fact]
    public void SegmentRetainsHashAndLengthButNotRawText()
    {
        const string content="secret prompt text";
        var segment=new TraceSegment(0,"user","text",content,5);
        Assert.Equal(content.Length,segment.CharacterCount);
        Assert.NotEmpty(segment.ContentHash);
        Assert.DoesNotContain("secret",string.Join("|",typeof(TraceSegment).GetProperties().Select(x=>x.Name)));
    }

    [Fact]
    public void InspectorFindsExactAndRetrievalDuplicatesButDoesNotGuessSemanticDuplicates()
    {
        var inspector=new ContextInspector(new HeuristicTokenCounter());
        var result=inspector.Inspect(new InspectionInput([
            new("system","text","Keep safety instructions."),
            new("user","retrieval","Source passage A"),
            new("user","retrieval","Source passage A"),
            new("assistant","text","The cat sat on the mat."),
            new("assistant","text","The cat slept on the mat.")
        ]),"unknown");
        Assert.Contains(result.Findings,x=>x.Category=="REPEATED_RETRIEVAL");
        Assert.DoesNotContain(result.Findings,x=>x.Category=="EXACT_DUPLICATE"&&x.AffectedSegmentIds.Contains(4));
    }

    [Fact]
    public void OptimizerCandidatePreservesSystemDeveloperAndUserMessages()
    {
        var inspector=new ContextInspector(new HeuristicTokenCounter());
        var result=inspector.Inspect(new InspectionInput([
            new("system","text","Rules"),new("assistant","text","Repeated response"),new("assistant","text","Repeated response"),new("user","text","Final question")
        ]),"unknown");
        var candidate=Assert.Single(result.Candidates);
        Assert.Contains("[system]",candidate.After);
        Assert.Contains("[user]",candidate.After);
        Assert.Equal("POTENTIAL",candidate.SavingsType);
        Assert.Contains("- Repeated response",candidate.Diff);
    }

    [Fact]
    public void PostgreSqlModelHasCascadeForeignKeysForProjectHistory()
    {
        var options=new DbContextOptionsBuilder<ContextBudgetDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options;
        using var db=new ContextBudgetDbContext(options);
        var project=db.Model.FindEntityType(typeof(Project))!;
        var trace=db.Model.FindEntityType(typeof(TraceRecord))!;
        var analysis=db.Model.FindEntityType(typeof(AnalysisRecord))!;
        Assert.Contains(project.GetReferencingForeignKeys(),fk=>fk.DeclaringEntityType.ClrType==typeof(TraceRecord)&&fk.DeleteBehavior==DeleteBehavior.Cascade);
        Assert.Contains(project.GetReferencingForeignKeys(),fk=>fk.DeclaringEntityType.ClrType==typeof(AnalysisRecord)&&fk.DeleteBehavior==DeleteBehavior.Cascade);
        Assert.Contains(trace.GetReferencingForeignKeys(),fk=>fk.DeclaringEntityType.ClrType==typeof(TraceSegment)&&fk.DeleteBehavior==DeleteBehavior.Cascade);
    }

    [Fact]
    public void RetrievalDedupPreservesDifferentSourceIdsAndWhitespaceLeavesCodeAlone()
    {
        var inspector=new ContextInspector(new HeuristicTokenCounter());
        var result=inspector.Inspect(new InspectionInput([
            new("user","retrieval","Same text","source-a"),
            new("user","retrieval","Same text","source-a"),
            new("user","retrieval","Same text","source-b"),
            new("assistant","text","A   sentence.\n\n\nNext."),
            new("assistant","code","x  =  1")
        ]),"unknown");
        Assert.Contains(result.Candidates,x=>x.Strategy=="RetrievalChunkDeduplication"&&x.ChangedSegmentIds.SequenceEqual([1]));
        Assert.Contains(result.Candidates,x=>x.Strategy=="WhitespaceAndFormattingOptimization");
        Assert.All(result.Candidates.Where(x=>x.Strategy=="RetrievalChunkDeduplication"),x=>Assert.Contains("source-a",x.Before));
    }

    [Fact]
    public void OfflineEvaluationReportsStructuralChecksButNeverVerifiesQuality()
    {
        var result=new OfflineEvaluationStrategy().Evaluate(new EvaluationRequest("Follow rule X","No rule",ExpectedOutput:"ok",CandidateResponse:"not json",RequiredFacts:["required"],ValidateJson:true,MandatoryInstructions:["rule X"]));
        Assert.Equal("OFFLINE",result.Mode);
        Assert.False(result.QualityVerified);
        Assert.Equal("NOT_VERIFIED",result.QualityStatus);
        Assert.Contains(result.Checks,x=>x.Name=="MANDATORY_INSTRUCTION_PRESERVATION"&&x.Status=="FAIL");
        Assert.Contains(result.Checks,x=>x.Name=="JSON_VALIDITY"&&x.Status=="FAIL");
    }

    [Fact]
    public void ToolCompactionCandidateSkipsJsonAndToolLinkedSegments()
    {
        var inspector=new ContextInspector(new HeuristicTokenCounter());
        var result=inspector.Inspect(new InspectionInput([
            new("tool","text","one\ntwo\none\nthree"),
            new("tool","json","{\"a\":1,\"a\":1}"),
            new("tool","text","one\ntwo\none\nthree",ToolCallId:"call-1")
        ]),"unknown");
        var candidate=Assert.Single(result.Candidates,x=>x.Strategy=="SafeToolOutputCompaction");
        Assert.Equal([0],candidate.ChangedSegmentIds);
        Assert.Equal("HIGH",candidate.Risk);
    }

    [Fact]
    public void BudgetAllocationKeepsCriticalInstructionsAndLatestUserRequest()
    {
        var strategy = new ContextBudgetAllocationStrategy(new ModelContextRegistry());
        var messages = new InspectionInput([
            new("system", "text", "Keep this rule."),
            new("user", "text", string.Join(' ', Enumerable.Repeat("history", 100))),
            new("assistant", "text", string.Join(' ', Enumerable.Repeat("response", 100))),
            new("developer", "text", "Never remove this."),
            new("tool", "text", string.Join(' ', Enumerable.Repeat("important", 20)), Priority: 80),
            new("user", "text", "Final question")
        ], TokenBudget: 55, OutputReserve: 5);
        var candidate = strategy.CreateCandidate(messages, "unknown-model", new HeuristicTokenCounter());
        Assert.NotNull(candidate);
        Assert.Contains("[system]", candidate.After);
        Assert.Contains("[developer]", candidate.After);
        Assert.Contains("important", candidate.After);
        Assert.Contains("Final question", candidate.After);
        Assert.DoesNotContain("history history", candidate.After);
        Assert.Equal("POTENTIAL", candidate.SavingsType);
    }

    [Fact]
    public void ModelProfilesExposeKnownContextWindowAndUnsupportedTokenizerStatus()
    {
        var registry = new ModelContextRegistry();
        Assert.Equal(128000, registry.Resolve("gpt-4o-mini").ContextWindowTokens);
        var unknown = registry.Resolve("private-local-model");
        Assert.False(unknown.TokenizerSupported);
        Assert.Equal("heuristic", unknown.TokenizerFamily);
    }

    [Fact]
    public void ModelAwareCounterUsesMatchingTokenizerAndFallsBackAsEstimated()
    {
        var tokenizer = new FixedTokenizer();
        var counter = new ModelAwareTokenCounter([tokenizer], new HeuristicTokenCounter());
        Assert.Equal("TOKENIZER:fixed-test", counter.Count("test-model-v1", "hello").Source);
        Assert.Equal("ESTIMATED", counter.Count("unsupported", "hello").Source);
    }

    private sealed class FixedTokenizer : IModelTokenizer
    {
        public string Name => "fixed-test";
        public bool Supports(string model) => model == "test-model-v1";
        public long Count(string model, string text) => 7;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONTEXTBUDGET_TEST_DB")))
            Skip = "Set CONTEXTBUDGET_TEST_DB to a disposable PostgreSQL test database to run this integration test.";
    }
}

public sealed class PostgreSqlIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task MigrationsProjectCrudAndTraceAnalysisWorkAgainstPostgreSql()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CONTEXTBUDGET_TEST_DB"));
        var schema = "cb_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            builder.SearchPath = schema;
            var options = new DbContextOptionsBuilder<ContextBudgetDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var db = new ContextBudgetDbContext(options);
            await db.Database.MigrateAsync();
            var projects = new ProjectRepository(db);
            var traces = new TraceRepository(db);
            var projectService = new ProjectService(projects);
            var traceService = new TraceService(traces, new HeuristicTokenCounter());

            var project = await projectService.Create(new ProjectInput("integration", "temporary schema"), CancellationToken.None);
            Assert.NotNull(await projectService.Get(project.Id, CancellationToken.None));
            var imported = await traceService.Import(project.Id, new TraceImportDto("trace-1", null, "mock", "unknown-model", null, "success", 4, new UsageDto(3, 2, null), [new MessageDto("user", "text", "hello"), new MessageDto("assistant", "text", "world")]), CancellationToken.None);

            Assert.Equal(2, imported.SegmentCount);
            Assert.Equal(3, imported.ProviderInputTokens);
            Assert.Single(await traceService.History(project.Id, CancellationToken.None));
            Assert.Equal(1, (await traces.Dashboard(CancellationToken.None)).TraceCount);

            var store = new ExperimentStore(db); var pricingStore = new PricingStore(db); var profile = new PricingProfile("fake", "fake-model", "USD", 1m, 2m, 0.5m, null, DateTimeOffset.UtcNow.AddMinutes(-1), null, "cached-included-in-input", "integration manual profile"); await pricingStore.CreateVersion(profile, CancellationToken.None); var snapshot = PricingService.Snapshot(profile); var pricedTraceService = new TraceService(traces, new HeuristicTokenCounter(), pricingStore); var pricedTrace = await pricedTraceService.Import(project.Id, new TraceImportDto("trace-priced", null, "fake", "fake-model", DateTimeOffset.UtcNow, "success", 5, new UsageDto(3, 2, 0), []), CancellationToken.None); Assert.NotEqual(Guid.Empty, pricedTrace.TraceRecordId); var datasetCases = new[] { new EvaluationCaseInput("private-1", "Synthetic", "private prompt marker", RequiredKeywords: ["private response marker"]) };
            var dataset = new EvaluationDataset(project.Id, "synthetic"); var version = new EvaluationDatasetVersion(dataset.Id, 1, 1, DatasetFingerprint.Of(datasetCases), DeterministicQualityEvaluator.Version); dataset.Versions.Add(version);
            await store.AddDataset(dataset, version, CancellationToken.None);
            var experiment = new Experiment(project.Id, version.Id, "persisted run", "fake", "fake-model", 1, DatasetFingerprint.Context("baseline private"), DatasetFingerprint.Context("optimized private"), null, 20);
            await store.AddExperiment(experiment, CancellationToken.None);
            var run = new ExperimentRun(experiment.Id, "integration-key"); run.SetPricingSnapshot(System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))); await store.AddRun(run, CancellationToken.None);
            var persistedCheck = new QualityCheck("RequiredKeyword", DeterministicQualityEvaluator.Version, "Passed", 1, "", "normal");
            var result = new ExperimentCaseResult(run.Id, "private-1", "Synthetic", 1, "B", "Succeeded", 4, 2, null, 5, 2, 8, System.Text.Json.JsonSerializer.Serialize(new[] { persistedCheck }), "");
            await store.SaveRun(run, [result], CancellationToken.None);
            var summary = ExperimentSummarizer.Build([new ExperimentCaseResultDto("private-1", "Synthetic", 1, "B", "Succeeded", 4, 2, null, 5, 2, 8, [persistedCheck], "")], 2, "PartiallyCompleted");
            await store.SaveRunState(run, experiment, "PartiallyCompleted", summary.QualityGate, System.Text.Json.JsonSerializer.Serialize(summary), CancellationToken.None);
            var persistedDataset = await store.Dataset(dataset.Id, CancellationToken.None);
            Assert.Equal(DatasetFingerprint.Of(datasetCases), Assert.Single(persistedDataset!.Versions).ContentHash);
            Assert.Equal("PartiallyCompleted", (await store.Run(run.Id, CancellationToken.None))!.Status); var historical = System.Text.Json.JsonSerializer.Deserialize<PricingSnapshot>((await store.Run(run.Id, CancellationToken.None))!.PricingSnapshotJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); Assert.Equal(1m, historical!.InputPricePerMillionTokens); var nextProfile = new PricingProfile("fake", "fake-model", "USD", 9m, 9m, 9m, null, DateTimeOffset.UtcNow.AddDays(1), null, "cached-included-in-input", "next manual profile"); await pricingStore.CreateVersion(nextProfile, CancellationToken.None); Assert.Equal(1m, System.Text.Json.JsonSerializer.Deserialize<PricingSnapshot>((await store.Run(run.Id, CancellationToken.None))!.PricingSnapshotJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.InputPricePerMillionTokens); Assert.Equal(2, (await pricingStore.List("fake", "fake-model", CancellationToken.None)).Count);
            var projectAnalytics = await new CostAnalyticsService(store, pricingStore, new CostCalculator(), new PerformanceAnalyticsService(), traces).Project(project.Id, CancellationToken.None);
            Assert.Single(projectAnalytics.ExperimentRuns);
            Assert.Equal(2, projectAnalytics.TraceCosts.TraceCount);
            Assert.Equal(1, projectAnalytics.TraceCosts.PricedTraceCount);
            Assert.Equal(1, projectAnalytics.TraceCosts.UnpricedTraceCount);
            Assert.Equal(0.000007m, Assert.Single(projectAnalytics.TraceCosts.CurrencyTotals).TotalCalculatedCost);
            var fakeProvider=new IntegrationFakeProvider();var fakeQueue=new IntegrationQueue();var experimentService=new ExperimentService(store,fakeQueue,fakeProvider);
            var workflowCases=new[]{new EvaluationCaseInput("workflow-1","Synthetic workflow","synthetic input",RequiredKeywords:["private response marker"])};
            var workflowDataset=await experimentService.CreateDataset(new DatasetCreateInput(project.Id,"offline workflow",workflowCases),CancellationToken.None);
            var workflowExperiment=await experimentService.CreateExperiment(new ExperimentCreateInput(project.Id,Assert.Single(workflowDataset.Versions).Id,"offline workflow","fake","fake-model",1,"baseline workflow context","optimized workflow context"),CancellationToken.None);
            var pendingRun=await experimentService.Start(workflowExperiment.Id,new ExperimentStartInput("workflow-key","baseline workflow context","optimized workflow context",workflowCases),CancellationToken.None);
            Assert.NotNull(fakeQueue.Job);
            await new ExperimentRunner(store,fakeProvider,new HeuristicTokenCounter(),new DeterministicQualityEvaluator()).Execute(fakeQueue.Job!,CancellationToken.None);
            var completedRun=await experimentService.GetRun(pendingRun.Id,CancellationToken.None);
            Assert.Equal("Completed",completedRun!.Status);
            Assert.Equal("PASSED_DEFINED_CHECKS",completedRun.QualityGate);
            Assert.Equal(2,completedRun.Results.Count);
            var retry=await experimentService.Start(workflowExperiment.Id,new ExperimentStartInput("workflow-key","baseline workflow context","optimized workflow context",workflowCases),CancellationToken.None);
            Assert.Equal(pendingRun.Id,retry.Id);
            Assert.Equal(1,fakeQueue.EnqueueCalls);
            var concurrent=await Task.WhenAll(
                experimentService.Start(workflowExperiment.Id,new ExperimentStartInput("concurrent-a","baseline workflow context","optimized workflow context",workflowCases),CancellationToken.None),
                experimentService.Start(workflowExperiment.Id,new ExperimentStartInput("concurrent-b","baseline workflow context","optimized workflow context",workflowCases),CancellationToken.None));
            Assert.Equal(concurrent[0].Id,concurrent[1].Id);
            Assert.Equal(2,fakeQueue.EnqueueCalls);
            await new ExperimentRunner(store,fakeProvider,new HeuristicTokenCounter(),new DeterministicQualityEvaluator()).Execute(fakeQueue.Job!,CancellationToken.None);
            await using(var restartedDb=new ContextBudgetDbContext(options))
            {
                var restartedTraces=new TraceRepository(restartedDb);var restartedExperiments=new ExperimentStore(restartedDb);var restartedPricing=new PricingStore(restartedDb);
                var persistedRun=await restartedExperiments.Run(run.Id,CancellationToken.None);
                Assert.NotNull(persistedRun);
                Assert.Equal("PartiallyCompleted",persistedRun.Status);
                Assert.Equal(1m,System.Text.Json.JsonSerializer.Deserialize<PricingSnapshot>(persistedRun.PricingSnapshotJson,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.InputPricePerMillionTokens);
                var restartedAnalytics=await new CostAnalyticsService(restartedExperiments,restartedPricing,new CostCalculator(),new PerformanceAnalyticsService(),restartedTraces).Project(project.Id,CancellationToken.None);
                Assert.Equal(2,restartedAnalytics.TraceCosts.TraceCount);
                Assert.Equal(3,restartedAnalytics.ExperimentRuns.Count);
                Assert.Equal(0.000007m,Assert.Single(restartedAnalytics.TraceCosts.CurrencyTotals).TotalCalculatedCost);
            }
            Assert.DoesNotContain("private prompt marker", string.Join("|", db.EvaluationDatasets.Select(x => x.Name), db.ExperimentCaseResults.Select(x => x.CaseName), db.ExperimentRuns.Select(x => x.SummaryJson)));
        }
        finally
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CONTEXTBUDGET_TEST_DB")).ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class IntegrationFakeProvider : ILLMProvider
    {
        public ProviderConfigurationDto GetConfiguration()=>new("fake","https://fake.invalid/v1","fake-model",true,true);
        public Task<LLMResponse> CompleteAsync(LLMRequest request,CancellationToken cancellationToken)=>Task.FromResult(new LLMResponse("fake","fake-model","private response marker",new ProviderUsage(3,2,0),null,4,"SUCCESS",null));
        public Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)=>Task.FromResult(new ProviderTestResult("fake","fake-model",0,null,"SUCCESS"));
    }

    private sealed class IntegrationQueue : IExperimentExecutionQueue
    {
        public ExperimentJob? Job { get; private set; }
        public int EnqueueCalls { get; private set; }
        public bool Enqueue(ExperimentJob job){Job=job;EnqueueCalls++;return true;}
        public bool Cancel(Guid runId)=>false;
    }
}
