using ContextBudget.Application;
using ContextBudget.Domain;
using Xunit;

namespace ContextBudget.Tests;

public sealed class CostAnalyticsTests
{
    private static PricingSnapshot Profile(decimal input=1m,decimal output=2m,decimal? cached=0.25m,decimal? write=null,string currency="USD")=>new(Guid.NewGuid(),"fake","model",currency,input,output,cached,write,DateTimeOffset.Parse("2026-01-01T00:00:00Z"),null,"cached-included-in-input","test profile");
    [Fact] public void CalculatorPricesUncachedCachedAndOutputTokensWithoutDoubleCounting()=>Assert.Equal(0.0000035m,new CostCalculator().Calculate(Profile(),new(3,1,2,0)).Breakdown.TotalCalculatedCost);
    [Fact] public void MissingUsageOrPriceDoesNotBecomeZero(){var c=new CostCalculator();Assert.Null(c.Calculate(Profile(),new(null,10,0,0)).Breakdown.TotalCalculatedCost);Assert.Equal("UNAVAILABLE",c.Calculate(null,new(1,1,0,0)).CalculationStatus);Assert.Null(c.Calculate(Profile(cached:null),new(1,1,1,0)).Breakdown.TotalCalculatedCost);}
    [Fact] public void CacheWriteUsageIsRequiredWhenASeparateRateIsConfigured(){var p=Profile(write:0.4m);var c=new CostCalculator();Assert.Equal(0.000005m,c.Calculate(p,new(3,0,0,5)).Breakdown.TotalCalculatedCost);Assert.Null(c.Calculate(p,new(3,0,0,null)).Breakdown.TotalCalculatedCost);}
    [Fact] public void ZeroTokensAndSmallDecimalsAreValid(){var x=new CostCalculator().Calculate(Profile(input:0.000001m,output:0.000003m),new(0,0,0,0));Assert.Equal(0m,x.Breakdown.TotalCalculatedCost);}
    [Fact] public void InvalidPricePrecisionAndUnsupportedBillingModeRejected(){var start=DateTimeOffset.UtcNow;Assert.Throws<ArgumentException>(()=>new PricingProfile("fake","model","USD",0.0000000000001m,1m,null,null,start,null,"cached-included-in-input","source"));Assert.Throws<ArgumentException>(()=>new PricingProfile("fake","model","USD",1m,1m,null,null,start,null,"separate","source"));}
    [Fact] public void InvalidUsageRejected(){Assert.Throws<ArgumentException>(()=>new CostCalculator().Calculate(Profile(),new(-1,0,0,0)));Assert.Throws<ArgumentException>(()=>new CostCalculator().Calculate(Profile(),new(1,1,2,0)));}
    [Fact] public void PairedLatencyExcludesFailuresAndP95RequiresSampleCount(){var rows=Enumerable.Range(0,5).SelectMany(i=>new[]{new ExperimentCaseResultDto($"c{i}","case",1,"A","Succeeded",1,1,0,null,null,100+i,[],""),new ExperimentCaseResultDto($"c{i}","case",1,"B",i==4?"Failed":"Succeeded",1,1,0,null,null,80+i,[],i==4?"timeout":"")}).ToArray();var x=new PerformanceAnalyticsService().Analyze(rows,5);Assert.Equal(4,x.PairedCount);Assert.Equal(102,x.Baseline.MedianLatencyMs);Assert.Null(x.Optimized.P95LatencyMs);Assert.Equal(0.2,x.Optimized.TimeoutRate);Assert.NotEmpty(x.SampleWarning);}
    [Fact] public void UnmatchedSamplesAreReportedSeparately(){var rows=new[]{new ExperimentCaseResultDto("a","a",1,"A","Succeeded",1,2,0,null,null,10,[],""),new ExperimentCaseResultDto("b","b",1,"B","Succeeded",1,2,0,null,null,10,[],"")};var x=new PerformanceAnalyticsService().Analyze(rows);Assert.Equal(0,x.PairedCount);Assert.Equal(2,x.UnpairedCount);}
}
