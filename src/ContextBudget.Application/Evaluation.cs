using System.Text.Json;

namespace ContextBudget.Application;

public sealed record EvaluationRequest(string OriginalContext,string CandidateContext,string? ExpectedOutput=null,string? OriginalResponse=null,string? CandidateResponse=null,IReadOnlyList<string>? RequiredFacts=null,bool ValidateJson=false,IReadOnlyList<string>? MandatoryInstructions=null);
public sealed record QualityCheckResult(string Name,string Status,string Details);
public sealed record EvaluationResult(string Mode,string QualityStatus,bool QualityVerified,IReadOnlyList<QualityCheckResult> Checks,string Disclaimer);
public interface IEvaluationStrategy { EvaluationResult Evaluate(EvaluationRequest request); }

public sealed class OfflineEvaluationStrategy:IEvaluationStrategy
{
    public EvaluationResult Evaluate(EvaluationRequest r)
    {
        var checks=new List<QualityCheckResult>();
        foreach(var instruction in r.MandatoryInstructions??[]){var present=r.CandidateContext.Contains(instruction,StringComparison.Ordinal);checks.Add(new("MANDATORY_INSTRUCTION_PRESERVATION",present?"PASS":"FAIL",present?"Zorunlu yönerge aday bağlamında bulundu.":"Zorunlu yönerge aday bağlamında bulunamadı."));}
        if(r.ExpectedOutput is not null&&r.CandidateResponse is not null){var exact=string.Equals(r.ExpectedOutput,r.CandidateResponse,StringComparison.Ordinal);checks.Add(new("EXACT_MATCH",exact?"PASS":"FAIL",exact?"Aday yanıt beklenen çıktıyla tam eşleşti.":"Aday yanıt beklenen çıktıyla tam eşleşmedi."));}
        foreach(var fact in r.RequiredFacts??[]){if(r.CandidateResponse is null){checks.Add(new("REQUIRED_FACT","SKIPPED",$"Yanıt sağlanmadığı için gerekli bilgi denetlenmedi: {fact}"));continue;}var contains=r.CandidateResponse.Contains(fact,StringComparison.OrdinalIgnoreCase);checks.Add(new("REQUIRED_FACT",contains?"PASS":"FAIL",contains?$"Gerekli bilgi bulundu: {fact}":$"Gerekli bilgi bulunamadı: {fact}"));}
        if(r.ValidateJson){if(r.CandidateResponse is null)checks.Add(new("JSON_VALIDITY","SKIPPED","Yanıt sağlanmadı."));else{try{using var _=JsonDocument.Parse(r.CandidateResponse);checks.Add(new("JSON_VALIDITY","PASS","Yanıt geçerli JSON."));}catch(JsonException){checks.Add(new("JSON_VALIDITY","FAIL","Yanıt geçerli JSON değil."));}}}
        if(checks.Count==0)checks.Add(new("STRUCTURAL_CHECKS","SKIPPED","Çalıştırılacak yapısal kontrol yapılandırılmadı."));
        return new("OFFLINE","NOT_VERIFIED",false,checks,"Offline yapısal kontroller gerçek LLM yanıt kalitesini ölçmez; kalite doğrulanmadı.");
    }
}
