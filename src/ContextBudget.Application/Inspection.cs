using System.Text;
using System.Text.RegularExpressions;

namespace ContextBudget.Application;

public sealed record InspectionInputMessage(string? Role,string? ContentType,string? Content,string? SourceId=null,string? ToolCallId=null,int? Priority=null);
public sealed record InspectionInput(IReadOnlyList<InspectionInputMessage> Messages,long? TokenBudget=null,long OutputReserve=0);
public sealed record FindingDto(string Category,string Severity,string Explanation,IReadOnlyList<int> AffectedSegmentIds,string SuggestedAction,long? EstimatedTokenOpportunity,string Confidence,string ApplicableStrategy);
public sealed record CandidateDto(string Strategy,string Before,string After,long EstimatedBefore,long EstimatedAfter,IReadOnlyList<int> ChangedSegmentIds,string Rationale,string Risk,string Diff,string SavingsType);
public sealed record InspectionResult(IReadOnlyList<DistributionDto> Distribution,IReadOnlyList<FindingDto> Findings,IReadOnlyList<CandidateDto> Candidates,long EstimatedInputTokens,long EstimatedOutputTokens);
public sealed record AnalyzeRequest(TraceImportDto Trace,long? TokenBudget=null,long OutputReserve=0);
public sealed record AnalysisRunDto(ImportResultDto Trace,InspectionResult Inspection);
public interface IOptimizationStrategy { CandidateDto? CreateCandidate(InspectionInput input,string model,ITokenCounter counter); }

public sealed record ModelContextProfile(string Model, int ContextWindowTokens, string TokenizerFamily, bool TokenizerSupported);
public interface IModelContextRegistry { ModelContextProfile Resolve(string model); }
public sealed class ModelContextRegistry : IModelContextRegistry
{
    private static readonly ModelContextProfile Unknown = new("unknown", 8192, "heuristic", false);
    public ModelContextProfile Resolve(string model)
    {
        model ??= "";
        var name = model.Trim().ToLowerInvariant();
        if (name.StartsWith("gpt-4o", StringComparison.Ordinal) || name.StartsWith("gpt-4.1", StringComparison.Ordinal)) return new(model, 128000, "o200k_base", false);
        if (name.StartsWith("gpt-4", StringComparison.Ordinal)) return new(model, 8192, "cl100k_base", false);
        if (name.StartsWith("claude-3", StringComparison.Ordinal) || name.StartsWith("claude-sonnet-4", StringComparison.Ordinal)) return new(model, 200000, "claude", false);
        if (name.StartsWith("gemini-", StringComparison.Ordinal)) return new(model, 1000000, "gemini", false);
        return Unknown;
    }
}

public sealed class ContextBudgetAllocationStrategy(IModelContextRegistry profiles) : IOptimizationStrategy
{
    public CandidateDto? CreateCandidate(InspectionInput input, string model, ITokenCounter counter)
    {
        var messages = input.Messages ?? [];
        var profile = profiles.Resolve(model);
        var budget = input.TokenBudget ?? profile.ContextWindowTokens;
        var capacity = Math.Max(0, budget - Math.Max(input.OutputReserve, 0));
        var counts = messages.Select(m => counter.Count(model, m.Content ?? "").Value).ToArray();
        var total = counts.Sum();
        if (total <= capacity || messages.Count == 0) return null;

        var keep = new HashSet<int>();
        for (var i = 0; i < messages.Count; i++)
            if (IsCritical(messages[i])) keep.Add(i);
        var latestUser = Enumerable.Range(0, messages.Count).LastOrDefault(i => IsRole(messages[i], "user"), -1);
        if (latestUser >= 0) keep.Add(latestUser);

        var used = keep.Sum(i => counts[i]);
        // Explicit priority wins; ties retain the newest segments first.
        foreach (var i in Enumerable.Range(0, messages.Count).OrderByDescending(i => messages[i].Priority ?? 0).ThenByDescending(i => i))
        {
            if (keep.Contains(i)) continue;
            if (used + counts[i] > capacity) continue;
            keep.Add(i); used += counts[i];
        }
        var removed = Enumerable.Range(0, messages.Count).Where(i => !keep.Contains(i)).ToHashSet();
        if (removed.Count == 0) return null;
        var before = InspectionText.Render(messages);
        var after = InspectionText.Render(messages.Where((_, i) => keep.Contains(i)).ToArray());
        var protectedOverflow = used > capacity;
        return new("ContextBudgetAllocation", before, after, total, messages.Where((_, i) => keep.Contains(i)).Sum(m => counter.Count(model, m.Content ?? "").Value), removed.Order().ToArray(),
            protectedOverflow ? "Kritik yönergeler ve son kullanıcı isteği korundu; korunan içerik tek başına hedef bütçeyi aşıyor." : $"Yaklaşık {capacity:N0} token giriş bütçesine en yeni bağlamı koruyarak indirgeme adayı.",
            protectedOverflow ? "HIGH" : "MEDIUM", InspectionText.Diff(before, after), "POTENTIAL");
    }
    private static bool IsCritical(InspectionInputMessage m) => IsRole(m, "system") || IsRole(m, "developer");
    private static bool IsRole(InspectionInputMessage m, string role) => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase);
}
public sealed class ExactDuplicateRemovalStrategy:IOptimizationStrategy
{
    public CandidateDto? CreateCandidate(InspectionInput input,string model,ITokenCounter counter)
    {
        var messages=input.Messages??[];var removed=new HashSet<int>();var seen=new HashSet<string>(StringComparer.Ordinal);
        for(var i=0;i<messages.Count;i++){var m=messages[i];if(!string.Equals(m.Role,"assistant",StringComparison.OrdinalIgnoreCase)||!string.IsNullOrWhiteSpace(m.ToolCallId)||string.IsNullOrWhiteSpace(m.Content))continue;if(!seen.Add(m.Content!))removed.Add(i);}
        if(removed.Count==0)return null;var before=InspectionText.Render(messages);var after=InspectionText.Render(messages.Where((_,i)=>!removed.Contains(i)).ToArray());
        return new("ExactDuplicateRemoval",before,after,messages.Sum(m=>counter.Count(model,m.Content??"").Value),messages.Where((_,i)=>!removed.Contains(i)).Sum(m=>counter.Count(model,m.Content??"").Value),removed.Order().ToArray(),"Birebir tekrar olan assistant segmentleri candidate olarak çıkarıldı.","MEDIUM",InspectionText.Diff(before,after),"POTENTIAL");
    }
}
public sealed class RetrievalChunkDeduplicationStrategy:IOptimizationStrategy
{
    public CandidateDto? CreateCandidate(InspectionInput input,string model,ITokenCounter counter)
    {
        var messages=input.Messages??[];var seen=new Dictionary<(string Content,string? Source),int>();var removed=new HashSet<int>();
        for(var i=0;i<messages.Count;i++){var m=messages[i];if(!string.Equals(m.ContentType,"retrieval",StringComparison.OrdinalIgnoreCase)||string.IsNullOrWhiteSpace(m.Content)||m.ToolCallId is not null)continue;var key=(m.Content!,m.SourceId);if(!seen.TryAdd(key,i))removed.Add(i);}
        return removed.Count==0?null:Candidate("RetrievalChunkDeduplication",messages,removed,"Aynı kimlikteki tekrar retrieval parçaları adaydan çıkarıldı.","LOW",model,counter);
    }
    internal static CandidateDto Candidate(string name,IReadOnlyList<InspectionInputMessage> messages,HashSet<int> removed,string reason,string risk,string model,ITokenCounter counter){var before=InspectionText.Render(messages);var after=InspectionText.Render(messages.Where((_,i)=>!removed.Contains(i)).ToArray());return new(name,before,after,messages.Sum(m=>counter.Count(model,m.Content??"").Value),messages.Where((_,i)=>!removed.Contains(i)).Sum(m=>counter.Count(model,m.Content??"").Value),removed.Order().ToArray(),reason,risk,InspectionText.Diff(before,after),"POTENTIAL");}
}
public sealed class WhitespaceAndFormattingOptimizationStrategy:IOptimizationStrategy
{
    public CandidateDto? CreateCandidate(InspectionInput input,string model,ITokenCounter counter)
    {
        var messages=input.Messages??[];var changed=new HashSet<int>();var optimized=messages.ToArray();
        for(var i=0;i<messages.Count;i++){var m=messages[i];var type=(m.ContentType??"text").ToLowerInvariant();var role=(m.Role??"").ToLowerInvariant();var text=m.Content??"";if(role!="assistant"||m.ToolCallId is not null||type.Contains("json")||type.Contains("code")||text.Contains("```")||text.TrimStart().StartsWith('{')||text.TrimStart().StartsWith('['))continue;var compact=Regex.Replace(Regex.Replace(text,"[ \\t]{2,}"," "),"\\n{3,}","\n\n").Trim();if(compact==text)continue;changed.Add(i);optimized[i]=m with{Content=compact};}
        if(changed.Count==0)return null;var before=InspectionText.Render(messages);var after=InspectionText.Render(optimized);return new("WhitespaceAndFormattingOptimization",before,after,messages.Sum(m=>counter.Count(model,m.Content??"").Value),optimized.Sum(m=>counter.Count(model,m.Content??"").Value),changed.Order().ToArray(),"Assistant düz metnindeki yinelenen boşluklar ve fazla boş satırlar azaltıldı.","LOW",InspectionText.Diff(before,after),"POTENTIAL");
    }
}
public sealed class SafeToolOutputCompactionStrategy:IOptimizationStrategy
{
    public CandidateDto? CreateCandidate(InspectionInput input,string model,ITokenCounter counter)
    {
        var messages=input.Messages??[];var changed=new HashSet<int>();var optimized=messages.ToArray();
        for(var i=0;i<messages.Count;i++){var m=messages[i];var type=(m.ContentType??"text").ToLowerInvariant();var text=m.Content??"";if(!string.Equals(m.Role,"tool",StringComparison.OrdinalIgnoreCase)||m.ToolCallId is not null||type.Contains("json")||type.Contains("code")||text.Contains("```")||text.TrimStart().StartsWith('{')||text.TrimStart().StartsWith('['))continue;var lines=text.Split('\n');if(lines.Length<4)continue;var unique=lines.Distinct(StringComparer.Ordinal).ToArray();if(unique.Length==lines.Length)continue;changed.Add(i);optimized[i]=m with{Content=string.Join('\n',unique)};}
        if(changed.Count==0)return null;var before=InspectionText.Render(messages);var after=InspectionText.Render(optimized);return new("SafeToolOutputCompaction",before,after,messages.Sum(m=>counter.Count(model,m.Content??"").Value),optimized.Sum(m=>counter.Count(model,m.Content??"").Value),changed.Order().ToArray(),"Yalnızca tam tekrarlanan satırlı düz metin tool çıktısı için inceleme adayı oluşturuldu; yapılandırılmış içerik ve tool-call ilişkileri korunuyor.","HIGH",InspectionText.Diff(before,after),"POTENTIAL");
    }
}
internal static class InspectionText
{
    public static string Render(IReadOnlyList<InspectionInputMessage> messages)=>string.Join("\n\n",messages.Select(m=>$"[{m.Role??"unknown"}{(m.SourceId is null?"":$" source={m.SourceId}")}{(m.ToolCallId is null?"":$" toolCall={m.ToolCallId}")}]\n{m.Content??""}"));
    public static string Diff(string before,string after){var oldLines=before.Split('\n');var newLines=after.Split('\n');var remainingNew=newLines.GroupBy(x=>x,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);var removed=new List<string>();foreach(var line in oldLines){if(remainingNew.TryGetValue(line,out var count)&&count>0)remainingNew[line]=count-1;else removed.Add(line);}var remainingOld=oldLines.GroupBy(x=>x,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);var added=new List<string>();foreach(var line in newLines){if(remainingOld.TryGetValue(line,out var count)&&count>0)remainingOld[line]=count-1;else added.Add(line);}return "--- original\n+++ candidate\n"+string.Join("\n",removed.Select(x=>"- "+x).Concat(added.Select(x=>"+ "+x)));}
}

public sealed class ContextInspector
{
    private readonly ITokenCounter counter;private readonly IEnumerable<IOptimizationStrategy> strategies;
    public ContextInspector(ITokenCounter counter,IEnumerable<IOptimizationStrategy>? strategies=null){this.counter=counter;this.strategies=strategies??[new ExactDuplicateRemovalStrategy(),new RetrievalChunkDeduplicationStrategy(),new WhitespaceAndFormattingOptimizationStrategy(),new SafeToolOutputCompactionStrategy(),new ContextBudgetAllocationStrategy(new ModelContextRegistry())];}
    public InspectionResult Inspect(InspectionInput input,string model)
    {
        var messages=input.Messages??[];var counts=messages.Select(m=>counter.Count(model,m.Content??"").Value).ToArray();
        var distribution=messages.Select((m,i)=>(Role:(m.Role??"unknown").ToLowerInvariant(),Count:counts[i])).GroupBy(x=>x.Role).Select(g=>new DistributionDto(g.Key,g.Sum(x=>x.Count),"ESTIMATED")).ToArray();
        var ins=messages.Select((m,i)=>(m,i)).Where(x=>!string.Equals(x.m.Role,"assistant",StringComparison.OrdinalIgnoreCase)).Sum(x=>counts[x.i]);
        var outs=messages.Select((m,i)=>(m,i)).Where(x=>string.Equals(x.m.Role,"assistant",StringComparison.OrdinalIgnoreCase)).Sum(x=>counts[x.i]);
        var findings=new List<FindingDto>();
        foreach(var group in messages.Select((m,i)=>(m,i)).Where(x=>!string.IsNullOrEmpty(x.m.Content)).GroupBy(x=>(x.m.Role??"unknown",x.m.Content!),new RoleContentComparer()).Where(g=>g.Count()>1))
        {var ids=group.Select(x=>x.i).ToArray();findings.Add(new("EXACT_DUPLICATE","MEDIUM","Aynı role ve içerikle tamamen aynı birden fazla segment bulundu.",ids,"System, developer yönergeleri ve son kullanıcı sorusunu koruyarak tekrarı gözden geçirin.",ids.Skip(1).Sum(i=>counts[i]),"HIGH","ExactDuplicateRemoval"));}
        foreach(var group in messages.Select((m,i)=>(m,i)).Where(x=>string.Equals(x.m.ContentType,"retrieval",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(x.m.Content)).GroupBy(x=>x.m.Content!,StringComparer.Ordinal).Where(g=>g.Count()>1))
        {var ids=group.Select(x=>x.i).ToArray();findings.Add(new("REPEATED_RETRIEVAL","MEDIUM","Aynı retrieval parçası birden fazla kez eklenmiş.",ids,"Tekrarlanan retrieval parçasını kaynak kimliğini koruyarak kaldırmayı değerlendirin.",ids.Skip(1).Sum(i=>counts[i]),"HIGH","RetrievalChunkDeduplication"));}
        foreach(var group in messages.Select((m,i)=>(m,i)).Where(x=>!string.IsNullOrEmpty(x.m.Content)).GroupBy(x=>(x.m.Role??"unknown",Normalize(x.m.Content!)),new RoleContentComparer()).Where(g=>g.Count()>1))
        {var distinct=group.Select(x=>x.m.Content).Distinct(StringComparer.Ordinal).Count();if(distinct<2)continue;var ids=group.Select(x=>x.i).ToArray();findings.Add(new("NORMALIZED_DUPLICATE_SUGGESTION","LOW","Büyük/küçük harf veya boşluk normalizasyonundan sonra benzer metinler bulundu; anlamsal eşitlik varsayılmadı.",ids,"İçeriklerin anlamını doğrulamadan birleştirmeyin.",null,"LOW","NormalizedDuplicateSuggestion"));}
        foreach(var (m,i) in messages.Select((m,i)=>(m,i)).Where(x=>string.Equals(x.m.Role,"tool",StringComparison.OrdinalIgnoreCase)&&counts[x.i]>500))findings.Add(new("OVERSIZED_TOOL_RESPONSE","HIGH","Araç yanıtı tahmini 500 tokenı aşıyor.",[i],"Yapılandırılmış alanları koruyan güvenli bir özetleme kuralını inceleyin.",null,"MEDIUM","SafeToolOutputCompaction"));
        if(input.TokenBudget is >0&&ins>Math.Max(0,input.TokenBudget.Value-input.OutputReserve)){var effective=Math.Max(0,input.TokenBudget.Value-input.OutputReserve);findings.Add(new("TOKEN_BUDGET_OVERFLOW","HIGH","Tahmini giriş bağlamı bütçe ve yanıt rezervini aşıyor.",Enumerable.Range(0,messages.Count).Where(i=>!string.Equals(messages[i].Role,"assistant",StringComparison.OrdinalIgnoreCase)).ToArray(),"Bütçe paylaştırmasını gözden geçirin; zorunlu yönergeleri koruyun.",ins-effective,"HIGH","ContextBudgetAllocation"));}
        if(messages.Count>=4&&ins>1000)findings.Add(new("CONVERSATION_GROWTH","INFO","Konuşma geçmişi tahmini 1.000 giriş tokenını aşıyor.",Enumerable.Range(0,messages.Count).Where(i=>!string.Equals(messages[i].Role,"assistant",StringComparison.OrdinalIgnoreCase)).ToArray(),"Konuşma büyümesini ve geçmiş özetleme politikasını gözden geçirin.",null,"LOW","ConversationGrowthAnalysis"));
        var candidates=strategies.Select(s=>s.CreateCandidate(input,model,counter)).Where(x=>x is not null).Cast<CandidateDto>().ToArray();
        foreach(var candidate in candidates)findings.Add(new("OPTIMIZATION_CANDIDATE","INFO",candidate.Rationale,candidate.ChangedSegmentIds,"Diff'i inceleyip kullanıcı onayıyla uygulayın.",candidate.EstimatedBefore-candidate.EstimatedAfter,"MEDIUM",candidate.Strategy));
        return new(distribution,findings,candidates,ins,outs);
    }
    private static string Normalize(string s)=>Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"\s+"," ",RegexOptions.CultureInvariant).Trim();
    private sealed class RoleContentComparer:IEqualityComparer<(string,string)>{public bool Equals((string,string) a,(string,string) b)=>a.Item1.Equals(b.Item1,StringComparison.OrdinalIgnoreCase)&&a.Item2==b.Item2;public int GetHashCode((string,string) x)=>HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item1),StringComparer.Ordinal.GetHashCode(x.Item2));}
}
