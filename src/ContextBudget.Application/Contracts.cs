using ContextBudget.Domain;
namespace ContextBudget.Application;
public sealed record ProjectDto(Guid Id,string Name,string? Description,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
public sealed record ProjectInput(string Name,string? Description);
public sealed record TraceImportDto(string? TraceId,string? CorrelationId,string? Provider,string? Model,DateTimeOffset? Timestamp,string? Status,double? LatencyMs,UsageDto? Usage,IReadOnlyList<MessageDto>? Messages);
public sealed record UsageDto(long? InputTokens,long? OutputTokens,long? CachedTokens);
public sealed record MessageDto(string? Role,string? ContentType,string? Content,string? SourceId=null,string? ToolCallId=null,int? Priority=null);
public sealed record DistributionDto(string Category,long Tokens,string Source);
public sealed record ImportResultDto(Guid TraceRecordId,int SegmentCount,long EstimatedInputTokens,long EstimatedOutputTokens,long? ProviderInputTokens,long? ProviderOutputTokens,string TokenSource,IReadOnlyList<DistributionDto> Distribution);
public sealed record AnalysisDto(Guid TraceRecordId,long EstimatedInputTokens,long? ProviderInputTokens,long EstimatedOutputTokens,long? ProviderOutputTokens,string TokenSource,IReadOnlyList<DistributionDto> Distribution,string QualityStatus,DateTimeOffset CreatedAt);
public sealed record DashboardDto(int ProjectCount,int TraceCount,long? EstimatedInputTokens,long? ProviderInputTokens,IReadOnlyList<RecentTraceDto> RecentTraces);
public sealed record RecentTraceDto(Guid Id,Guid ProjectId,string Provider,string Model,DateTimeOffset Timestamp,long? InputTokens,long? EstimatedInputTokens);
public interface IProjectRepository { Task<IReadOnlyList<Project>> List(CancellationToken ct); Task<Project?> Get(Guid id,CancellationToken ct); Task Add(Project project,CancellationToken ct); Task Save(CancellationToken ct); Task Delete(Project project,CancellationToken ct); }
public interface ITraceRepository { Task<bool> ProjectExists(Guid id,CancellationToken ct); Task Add(TraceRecord trace,AnalysisRecord analysis,CancellationToken ct); Task<IReadOnlyList<AnalysisRecord>> Analyses(Guid projectId,CancellationToken ct); Task<DashboardDto> Dashboard(CancellationToken ct); Task<IReadOnlyList<TraceRecord>> ProjectTracesForCostAnalytics(Guid projectId,CancellationToken ct)=>Task.FromResult<IReadOnlyList<TraceRecord>>([]); }
public interface ITokenCounter { TokenCount Count(string model,string text); }
public sealed record TokenCount(long Value,string Source);
public interface IModelTokenizer { bool Supports(string model); long Count(string model,string text); string Name { get; } }
public sealed class ProjectService(IProjectRepository repository)
{
    public async Task<IReadOnlyList<ProjectDto>> List(CancellationToken ct)=>(await repository.List(ct)).Select(ToDto).ToArray();
    public async Task<ProjectDto?> Get(Guid id,CancellationToken ct){var p=await repository.Get(id,ct);return p is null?null:ToDto(p);}
    public async Task<ProjectDto> Create(ProjectInput input,CancellationToken ct){var p=new Project(input.Name,input.Description);await repository.Add(p,ct);await repository.Save(ct);return ToDto(p);}
    public async Task<ProjectDto?> Update(Guid id,ProjectInput input,CancellationToken ct){var p=await repository.Get(id,ct);if(p is null)return null;p.Update(input.Name,input.Description);await repository.Save(ct);return ToDto(p);}
    public async Task<bool> Delete(Guid id,CancellationToken ct){var p=await repository.Get(id,ct);if(p is null)return false;await repository.Delete(p,ct);await repository.Save(ct);return true;}
    private static ProjectDto ToDto(Project p)=>new(p.Id,p.Name,p.Description,p.CreatedAt,p.UpdatedAt);
}
public sealed class TraceService(ITraceRepository traces,ITokenCounter counter,IPricingStore? pricing=null)
{
    public async Task<ImportResultDto> Import(Guid projectId,TraceImportDto input,CancellationToken ct)
    {
        if(!await traces.ProjectExists(projectId,ct))throw new KeyNotFoundException("Project not found.");
        var messages=input.Messages??[];var model=string.IsNullOrWhiteSpace(input.Model)?"unknown":input.Model.Trim();var provider=string.IsNullOrWhiteSpace(input.Provider)?"unknown":input.Provider.Trim();var timestamp=input.Timestamp??DateTimeOffset.UtcNow;
        var activeProfile=pricing is null?null:await pricing.FindActive(provider,model,timestamp,ct);
        var snapshot=activeProfile is null?"null":System.Text.Json.JsonSerializer.Serialize(PricingService.Snapshot(activeProfile),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var record=new TraceRecord(projectId,input.TraceId,input.CorrelationId,provider,model,timestamp,input.Status??"unknown",input.LatencyMs,input.Usage?.InputTokens,input.Usage?.OutputTokens,input.Usage?.CachedTokens,snapshot);
        long ins=0,outs=0;var buckets=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<messages.Count;i++){var m=messages[i];var content=m.Content??"";var role=(m.Role??"unknown").ToLowerInvariant();var count=counter.Count(model,content).Value;record.Segments.Add(new TraceSegment(i,role,m.ContentType??"text",content,count,m.SourceId,m.ToolCallId));buckets[role]=buckets.GetValueOrDefault(role)+count;if(role=="assistant")outs+=count;else ins+=count;}
        var analysis=new AnalysisRecord(projectId,record.Id,model,ins,input.Usage?.InputTokens,outs,input.Usage?.OutputTokens);await traces.Add(record,analysis,ct);
        var distribution=buckets.Select(x=>new DistributionDto(x.Key,x.Value,"ESTIMATED")).ToArray();
        return new(record.Id,messages.Count,ins,outs,input.Usage?.InputTokens,input.Usage?.OutputTokens,"ESTIMATED",distribution);
    }
    public async Task<IReadOnlyList<AnalysisDto>> History(Guid id,CancellationToken ct)=>(await traces.Analyses(id,ct)).Select(a=>new AnalysisDto(a.TraceRecordId,a.EstimatedInputTokens,a.ProviderInputTokens,a.EstimatedOutputTokens,a.ProviderOutputTokens,"ESTIMATED",[],"NOT_VERIFIED",a.CreatedAt)).ToArray();
}
public sealed class HeuristicTokenCounter:ITokenCounter
{
    public TokenCount Count(string model,string text){if(string.IsNullOrEmpty(text))return new(0,"ESTIMATED");var words=System.Text.RegularExpressions.Regex.Matches(text,@"[\p{L}\p{N}_]+|[^\s]",System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count;return new(Math.Max(1,(long)Math.Ceiling(words*1.12)),"ESTIMATED");}
}
public sealed class ModelAwareTokenCounter(IEnumerable<IModelTokenizer> tokenizers, HeuristicTokenCounter fallback) : ITokenCounter
{
    public TokenCount Count(string model, string text)
    {
        var tokenizer = tokenizers.FirstOrDefault(t => t.Supports(model));
        return tokenizer is null ? fallback.Count(model, text) : new(Math.Max(0, tokenizer.Count(model, text)), $"TOKENIZER:{tokenizer.Name}");
    }
}
