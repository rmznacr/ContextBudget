using ContextBudget.Domain;
using System.Text.Json;

namespace ContextBudget.Application;

public sealed record PricingProfileInput(string Provider,string ModelId,string Currency,decimal InputPricePerMillionTokens,decimal OutputPricePerMillionTokens,decimal? CachedInputPricePerMillionTokens,decimal? CacheWritePricePerMillionTokens,DateTimeOffset EffectiveFromUtc,DateTimeOffset? EffectiveToUtc,string PricingMode,string SourceDescription);
public sealed record PricingProfileDto(Guid Id,string Provider,string ModelId,string Currency,decimal InputPricePerMillionTokens,decimal OutputPricePerMillionTokens,decimal? CachedInputPricePerMillionTokens,decimal? CacheWritePricePerMillionTokens,DateTimeOffset EffectiveFromUtc,DateTimeOffset? EffectiveToUtc,string PricingMode,string SourceDescription,DateTimeOffset CreatedAtUtc);
public sealed record PricingSnapshot(Guid Id,string Provider,string ModelId,string Currency,decimal InputPricePerMillionTokens,decimal OutputPricePerMillionTokens,decimal? CachedInputPricePerMillionTokens,decimal? CacheWritePricePerMillionTokens,DateTimeOffset EffectiveFromUtc,DateTimeOffset? EffectiveToUtc,string PricingMode,string SourceDescription);
public sealed record CallCost(long? InputTokens,long? OutputTokens,long? CachedInputTokens,long? CacheWriteTokens,string UsageSource="PROVIDER_REPORTED");
public sealed record CostBreakdown(decimal? InputCost,decimal? CachedInputCost,decimal? CacheWriteCost,decimal? OutputCost,decimal? TotalCalculatedCost);
public sealed record CostResult(Guid? PricingProfileId,string? Currency,string UsageSource,string CostSource,string CalculationStatus,CostBreakdown Breakdown,IReadOnlyList<string> MissingInputs,PricingSnapshot? PricingSnapshot);
public sealed record CostCalculationInput(string Provider,string ModelId,long? InputTokens,long? OutputTokens,long? CachedInputTokens,long? CacheWriteTokens,DateTimeOffset? AtUtc=null);
public interface ICostCalculator { CostResult Calculate(PricingSnapshot? profile,CallCost usage); }
public sealed class CostCalculator : ICostCalculator
{
    private const decimal Million=1_000_000m;
    public CostResult Calculate(PricingSnapshot? p,CallCost u)
    {
        if(p is null) return new(null,null,u.UsageSource,"CALCULATED","UNAVAILABLE",new(null,null,null,null,null),["pricing_profile"],null);
        if(u.InputTokens<0||u.OutputTokens<0||u.CachedInputTokens<0||u.CacheWriteTokens<0) throw new ArgumentException("Token usage cannot be negative.");
        var missing=new List<string>(); if(!string.Equals(p.PricingMode,"cached-included-in-input",StringComparison.Ordinal)) missing.Add("unsupported_pricing_mode");
        if(u.InputTokens is null) missing.Add("input_tokens"); if(u.OutputTokens is null) missing.Add("output_tokens");
        if(u.CachedInputTokens is null) missing.Add("cached_input_tokens"); if(u.CacheWriteTokens is null && p.CacheWritePricePerMillionTokens is not null) missing.Add("cache_write_tokens");
        if(p.CachedInputPricePerMillionTokens is null && (u.CachedInputTokens is null || u.CachedInputTokens > 0)) missing.Add("cached_input_price");
        if(p.CacheWritePricePerMillionTokens is null && u.CacheWriteTokens is > 0) missing.Add("cache_write_price");
        if(u.InputTokens is not null && u.CachedInputTokens is not null && u.CachedInputTokens > u.InputTokens) throw new ArgumentException("Cached input tokens cannot exceed total input tokens.");
        decimal? input=null,cached=null,write=null,output=null;
        if(u.InputTokens.HasValue&&u.CachedInputTokens.HasValue&&u.CachedInputTokens<=u.InputTokens&&(u.CachedInputTokens==0||p.CachedInputPricePerMillionTokens.HasValue)) input=(u.InputTokens.Value-u.CachedInputTokens.Value)*p.InputPricePerMillionTokens/Million;
        if(u.CachedInputTokens.HasValue) cached=u.CachedInputTokens.Value==0&&p.CachedInputPricePerMillionTokens is null?0m:p.CachedInputPricePerMillionTokens.HasValue?u.CachedInputTokens.Value*p.CachedInputPricePerMillionTokens.Value/Million:null;
        if(u.CacheWriteTokens.HasValue&&p.CacheWritePricePerMillionTokens.HasValue) write=u.CacheWriteTokens.Value*p.CacheWritePricePerMillionTokens.Value/Million;
        else if(p.CacheWritePricePerMillionTokens is null&&p.PricingMode=="cached-included-in-input") write=0m;
        if(u.OutputTokens.HasValue) output=u.OutputTokens.Value*p.OutputPricePerMillionTokens/Million;
        var total=missing.Count==0&&input.HasValue&&cached.HasValue&&write.HasValue&&output.HasValue?input+cached+write+output:null;
        return new(p.Id,p.Currency,u.UsageSource,"CALCULATED",missing.Count==0?"CALCULATED":"INCOMPLETE",new(input,cached,write,output,total),missing,p);
    }
}
public sealed record VariantPerformance(string Variant,int SampleCount,int SuccessfulCalls,int FailedCalls,int TimeoutCalls,double? FailureRate,double? TimeoutRate,double? AverageLatencyMs,double? MedianLatencyMs,double? P95LatencyMs,string P95Status,long? InputTokens,long? OutputTokens,long? CachedTokens,long? TotalTokens);
public sealed record ExperimentPerformance(int PairedCount,int UnpairedCount,long? PairedLatencyDifferenceMs,long? InputTokenDifference,long? OutputTokenDifference,long? TotalTokenDifference,long? CachedTokenDifference,VariantPerformance Baseline,VariantPerformance Optimized,string SampleWarning);
public sealed record VariantCosts(decimal? InputCost,decimal? CachedInputCost,decimal? CacheWriteCost,decimal? OutputCost,decimal? TotalCalculatedCost,int PricedCalls,int UnpricedCalls,string? Currency,IReadOnlyList<string> MissingInputs);
public sealed record ExperimentCostSummary(Guid RunId,Guid ProjectId,string Provider,string ModelId,DateTimeOffset RunCreatedAtUtc,Guid? PricingProfileId,string? Currency,string CostSource,VariantCosts Baseline,VariantCosts Optimized,decimal? AbsoluteCostDifference,decimal? PercentageCostDifference,string QualityGate,string InvestmentCostStatus,string? InvestmentCostExplanation);
public sealed record ExperimentComparisonDto(ExperimentCostSummary Cost,ExperimentPerformance Performance);
public sealed record TraceCostDto(Guid TraceId,string Provider,string ModelId,DateTimeOffset Timestamp,string Status,long? InputTokens,long? OutputTokens,long? CachedInputTokens,CostResult Cost);
public sealed record TraceCurrencyTotal(string Currency,int PricedTraces,int UnpricedTraces,decimal TotalCalculatedCost);
public sealed record ProjectTraceCostAnalytics(int TraceCount,int PricedTraceCount,int UnpricedTraceCount,IReadOnlyList<TraceCurrencyTotal> CurrencyTotals,IReadOnlyList<TraceCostDto> Traces);
public sealed record ProjectAnalyticsDto(Guid ProjectId,IReadOnlyList<ExperimentComparisonDto> ExperimentRuns,ProjectTraceCostAnalytics TraceCosts);
public interface IPricingStore
{
    Task<IReadOnlyList<PricingProfile>> List(string? provider,string? model,CancellationToken ct);
    Task<PricingProfile?> Get(Guid id,CancellationToken ct);
    Task<PricingProfile?> FindActive(string provider,string model,DateTimeOffset at,CancellationToken ct);
    Task CreateVersion(PricingProfile profile,CancellationToken ct);
    Task<IReadOnlyList<ExperimentRun>> ProjectRuns(Guid projectId,CancellationToken ct);
}
public sealed class PricingService(IPricingStore store)
{
    public async Task<IReadOnlyList<PricingProfileDto>> List(string? provider,string? model,CancellationToken ct)=>(await store.List(provider,model,ct)).Select(ToDto).ToArray();
    public async Task<PricingProfileDto?> Get(Guid id,CancellationToken ct){var p=await store.Get(id,ct);return p is null?null:ToDto(p);}
    public async Task<PricingProfileDto> Create(PricingProfileInput x,CancellationToken ct){var p=new PricingProfile(x.Provider,x.ModelId,x.Currency,x.InputPricePerMillionTokens,x.OutputPricePerMillionTokens,x.CachedInputPricePerMillionTokens,x.CacheWritePricePerMillionTokens,x.EffectiveFromUtc,x.EffectiveToUtc,x.PricingMode,x.SourceDescription);await store.CreateVersion(p,ct);return ToDto(p);}
    public static PricingSnapshot Snapshot(PricingProfile x)=>new(x.Id,x.Provider,x.ModelId,x.Currency,x.InputPricePerMillionTokens,x.OutputPricePerMillionTokens,x.CachedInputPricePerMillionTokens,x.CacheWritePricePerMillionTokens,x.EffectiveFromUtc,x.EffectiveToUtc,x.PricingMode,x.SourceDescription);
    public static PricingProfileDto ToDto(PricingProfile x)=>new(x.Id,x.Provider,x.ModelId,x.Currency,x.InputPricePerMillionTokens,x.OutputPricePerMillionTokens,x.CachedInputPricePerMillionTokens,x.CacheWritePricePerMillionTokens,x.EffectiveFromUtc,x.EffectiveToUtc,x.PricingMode,x.SourceDescription,x.CreatedAtUtc);
}
public sealed class PerformanceAnalyticsService
{
    public ExperimentPerformance Analyze(IReadOnlyList<ExperimentCaseResultDto> rows,int minimumP95Samples=20)
    {
        if(minimumP95Samples<2) throw new ArgumentOutOfRangeException(nameof(minimumP95Samples));
        var a=Variant(rows,"A",minimumP95Samples);var b=Variant(rows,"B",minimumP95Samples);
        var groups=rows.GroupBy(x=>(x.CaseId,x.Repeat)).ToArray();var paired=groups.Where(g=>g.Any(x=>x.Variant=="A")&&g.Any(x=>x.Variant=="B")).ToArray();var latencies=paired.Select(g=>(A:g.First(x=>x.Variant=="A"),B:g.First(x=>x.Variant=="B"))).Where(p=>p.A.Status=="Succeeded"&&p.B.Status=="Succeeded").ToArray();
        var la=latencies.Select(x=>(long?)x.A.LatencyMs).ToArray();var lb=latencies.Select(x=>(long?)x.B.LatencyMs).ToArray();
        var pairedRows=paired.Select(g=>(A:g.First(x=>x.Variant=="A"),B:g.First(x=>x.Variant=="B"))).Where(x=>x.A.Status=="Succeeded"&&x.B.Status=="Succeeded").ToArray();
        return new(latencies.Length,groups.Length-latencies.Length,la.Length>0? (long)Math.Round(la.Average(x=>x!.Value)-lb.Average(x=>x!.Value)):null,PairedDifference(pairedRows,x=>x.A.ProviderInputTokens,x=>x.B.ProviderInputTokens),PairedDifference(pairedRows,x=>x.A.ProviderOutputTokens,x=>x.B.ProviderOutputTokens),PairedDifference(pairedRows,x=>Add(x.A.ProviderInputTokens,x.A.ProviderOutputTokens),x=>Add(x.B.ProviderInputTokens,x.B.ProviderOutputTokens)),PairedDifference(pairedRows,x=>x.A.CachedTokens,x=>x.B.CachedTokens),a,b,Math.Min(a.SuccessfulCalls,b.SuccessfulCalls)<minimumP95Samples?$"Small sample: p95 requires {minimumP95Samples} successful calls per variant.":"");
    }
    private static VariantPerformance Variant(IReadOnlyList<ExperimentCaseResultDto> all,string variant,int min){var r=all.Where(x=>x.Variant==variant).ToArray();var ok=r.Where(x=>x.Status=="Succeeded").ToArray();var lat=ok.Select(x=>(double)x.LatencyMs).Order().ToArray();var attempted=r.Where(x=>x.Status!="Cancelled").ToArray();var timeouts=attempted.Count(x=>x.FailureCode.Equals("timeout",StringComparison.OrdinalIgnoreCase));var fail=attempted.Count(x=>x.Status!="Succeeded");return new(variant,r.Length,ok.Length,fail,timeouts,attempted.Length==0?null:(double)fail/attempted.Length,attempted.Length==0?null:(double)timeouts/attempted.Length,lat.Length==0?null:lat.Average(),Percentile(lat,.5),lat.Length<min?null:Percentile(lat,.95),lat.Length<min?"INSUFFICIENT_SAMPLES":"AVAILABLE",Sum(r,x=>x.ProviderInputTokens),Sum(r,x=>x.ProviderOutputTokens),Sum(r,x=>x.CachedTokens),Add(Sum(r,x=>x.ProviderInputTokens),Sum(r,x=>x.ProviderOutputTokens)));}
    private static double? Percentile(double[] x,double p){if(x.Length==0)return null;if(p==.5&&x.Length%2==0)return (x[x.Length/2-1]+x[x.Length/2])/2;return x[Math.Clamp((int)Math.Ceiling(p*x.Length)-1,0,x.Length-1)];}
    private static long? Sum(ExperimentCaseResultDto[] x,Func<ExperimentCaseResultDto,long?> get)=>x.Length>0&&x.All(y=>get(y).HasValue)?x.Sum(y=>get(y)!.Value):null;
    private static long? Add(long? x,long? y)=>x.HasValue&&y.HasValue?x+y:null;private static long? Diff(long? a,long? b)=>a.HasValue&&b.HasValue?a-b:null;
    private static long? PairedDifference<T>(T[] pairs,Func<T,long?> a,Func<T,long?> b)=>pairs.Length>0&&pairs.All(x=>a(x).HasValue&&b(x).HasValue)?pairs.Sum(x=>a(x)!.Value-b(x)!.Value):null;
}
public sealed class CostAnalyticsService(IExperimentStore experiments,IPricingStore pricing,ICostCalculator calculator,PerformanceAnalyticsService performance,ITraceRepository traces)
{
    public async Task<CostResult> Calculate(CostCalculationInput input,CancellationToken ct){if(string.IsNullOrWhiteSpace(input.Provider)||string.IsNullOrWhiteSpace(input.ModelId))throw new ArgumentException("Provider and model are required.");var at=input.AtUtc??DateTimeOffset.UtcNow;var profile=await pricing.FindActive(input.Provider,input.ModelId,at,ct);return calculator.Calculate(profile is null?null:PricingService.Snapshot(profile),new(input.InputTokens,input.OutputTokens,input.CachedInputTokens,input.CacheWriteTokens));}
    public async Task<ExperimentComparisonDto?> Compare(Guid id,CancellationToken ct){var run=await experiments.Run(id,ct);return run is null?null:Build(run);}
    public async Task<ExperimentComparisonDto?> CompareExperiment(Guid id,CancellationToken ct){var e=await experiments.Experiment(id,ct);if(e is null)return null;var runs=await experiments.Runs(id,ct);var latest=runs.FirstOrDefault();return latest is null?null:await Compare(latest.Id,ct);}
    public async Task<ProjectAnalyticsDto> Project(Guid id,CancellationToken ct)
    {
        var runs=await pricing.ProjectRuns(id,ct);var traceRows=await traces.ProjectTracesForCostAnalytics(id,ct);
        var items=traceRows.Select(TraceCost).ToArray();
        var totals=items.Where(x=>x.Cost.Breakdown.TotalCalculatedCost.HasValue&&x.Cost.Currency is not null)
            .GroupBy(x=>x.Cost.Currency!,StringComparer.OrdinalIgnoreCase)
            .Select(g=>new TraceCurrencyTotal(g.Key,g.Count(),items.Count(x=>string.Equals(x.Cost.Currency,g.Key,StringComparison.OrdinalIgnoreCase)&&!x.Cost.Breakdown.TotalCalculatedCost.HasValue),g.Sum(x=>x.Cost.Breakdown.TotalCalculatedCost!.Value))).ToArray();
        return new(id,runs.Select(Build).ToArray(),new(items.Length,items.Count(x=>x.Cost.Breakdown.TotalCalculatedCost.HasValue),items.Count(x=>!x.Cost.Breakdown.TotalCalculatedCost.HasValue),totals,items));
    }
    private TraceCostDto TraceCost(TraceRecord trace)
    {
        PricingSnapshot? snapshot=null;
        try{if(trace.PricingSnapshotJson!="null")snapshot=JsonSerializer.Deserialize<PricingSnapshot>(trace.PricingSnapshotJson,new JsonSerializerOptions(JsonSerializerDefaults.Web));}catch(JsonException){}
        CostResult result;
        if(trace.ProviderInputTokens<0||trace.ProviderOutputTokens<0||trace.CachedTokens<0)
            result=new(snapshot?.Id,snapshot?.Currency,"PROVIDER_REPORTED","CALCULATED","INVALID_USAGE",new(null,null,null,null,null),["invalid_token_usage"],snapshot);
        else result=calculator.Calculate(snapshot,new(trace.ProviderInputTokens,trace.ProviderOutputTokens,trace.CachedTokens,null));
        return new(trace.Id,trace.Provider,trace.Model,trace.Timestamp,trace.Status,trace.ProviderInputTokens,trace.ProviderOutputTokens,trace.CachedTokens,result);
    }
    private ExperimentComparisonDto Build(ExperimentRun run)
    {
        var rows=run.Results.Select(x=>new ExperimentCaseResultDto(x.CaseId,x.CaseName,x.Repeat,x.Variant,x.Status,x.ProviderInputTokens,x.ProviderOutputTokens,x.CachedTokens,x.EstimatedInputTokens,x.EstimatedOutputTokens,x.LatencyMs,JsonSerializer.Deserialize<QualityCheck[]>(x.ChecksJson)??[],x.FailureCode)).ToArray();
        PricingSnapshot? snap=null;try{if(run.PricingSnapshotJson!="null")snap=JsonSerializer.Deserialize<PricingSnapshot>(run.PricingSnapshotJson,new JsonSerializerOptions(JsonSerializerDefaults.Web));}catch(JsonException){}
        var aa=Costs(rows.Where(x=>x.Variant=="A"),snap);var bb=Costs(rows.Where(x=>x.Variant=="B"),snap);var diff=aa.TotalCalculatedCost.HasValue&&bb.TotalCalculatedCost.HasValue&&aa.Currency==bb.Currency?aa.TotalCalculatedCost-bb.TotalCalculatedCost:null;
        decimal? pct=diff.HasValue&&aa.TotalCalculatedCost is >0?diff.Value*100m/aa.TotalCalculatedCost.Value:null;
        var quality=run.QualityGate;
        return new(new(run.Id,run.Experiment?.ProjectId??Guid.Empty,run.Experiment?.Provider??"unknown",run.Experiment?.Model??"unknown",run.CreatedAtUtc,snap?.Id,snap?.Currency,"CALCULATED",aa,bb,diff,pct,quality,"UNMEASURED","Generation, judge, and other preparation costs were not persisted for this run."),performance.Analyze(rows));
    }
    private VariantCosts Costs(IEnumerable<ExperimentCaseResultDto> rows,PricingSnapshot? p){var costs=rows.Select(x=>calculator.Calculate(p,new(x.ProviderInputTokens,x.ProviderOutputTokens,x.CachedTokens,null))).ToArray();return new(costs.Length>0&&costs.All(x=>x.Breakdown.InputCost.HasValue)?costs.Sum(x=>x.Breakdown.InputCost!.Value):null,costs.Length>0&&costs.All(x=>x.Breakdown.CachedInputCost.HasValue)?costs.Sum(x=>x.Breakdown.CachedInputCost!.Value):null,costs.Length>0&&costs.All(x=>x.Breakdown.CacheWriteCost.HasValue)?costs.Sum(x=>x.Breakdown.CacheWriteCost!.Value):null,costs.Length>0&&costs.All(x=>x.Breakdown.OutputCost.HasValue)?costs.Sum(x=>x.Breakdown.OutputCost!.Value):null,costs.Length>0&&costs.All(x=>x.Breakdown.TotalCalculatedCost.HasValue)?costs.Sum(x=>x.Breakdown.TotalCalculatedCost!.Value):null,costs.Count(x=>x.Breakdown.TotalCalculatedCost.HasValue),costs.Count(x=>!x.Breakdown.TotalCalculatedCost.HasValue),p?.Currency,costs.SelectMany(x=>x.MissingInputs).Distinct().ToArray());}
}
