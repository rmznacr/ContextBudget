using System.Text.Json;
using System.Data.Common;
using ContextBudget.Application;
using ContextBudget.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var providerOptions=builder.Configuration.GetSection("Providers:OpenAICompatible").Get<LLMProviderOptions>()??new LLMProviderOptions();
builder.Services.AddSingleton(providerOptions);
builder.Services.AddSingleton<IProviderDnsResolver,SystemProviderDnsResolver>();
builder.Services.AddSingleton<ProviderConnectionPolicy>();
builder.Services.AddSingleton(sp=>
{
    var providerPolicy=sp.GetRequiredService<ProviderConnectionPolicy>();
    var handler=providerPolicy.CreateHttpHandler();
    var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(Math.Clamp(providerOptions.TimeoutSeconds,1,120))};
    return client;
});
builder.Services.AddSingleton<ILLMProvider,OpenAICompatibleProvider>();
var connection=builder.Configuration.GetConnectionString("Postgres")??throw new InvalidOperationException("ConnectionStrings:Postgres must be configured.");
builder.Services.AddDbContext<ContextBudgetDbContext>(o=>o.UseNpgsql(connection));
builder.Services.AddScoped<IExperimentStore,ExperimentStore>();builder.Services.AddScoped<IPricingStore,PricingStore>();builder.Services.AddScoped<PricingService>();builder.Services.AddSingleton<ICostCalculator,CostCalculator>();builder.Services.AddSingleton<PerformanceAnalyticsService>();builder.Services.AddScoped<CostAnalyticsService>();builder.Services.AddSingleton<DeterministicQualityEvaluator>();builder.Services.AddScoped<ExperimentService>();builder.Services.AddScoped<ExperimentRunner>();builder.Services.AddSingleton<ExperimentExecutionQueue>();builder.Services.AddSingleton<IExperimentExecutionQueue>(sp=>sp.GetRequiredService<ExperimentExecutionQueue>());builder.Services.AddHostedService(sp=>sp.GetRequiredService<ExperimentExecutionQueue>());
builder.Services.AddScoped<IProjectRepository,ProjectRepository>();builder.Services.AddScoped<ITraceRepository,TraceRepository>();builder.Services.AddSingleton<IModelContextRegistry,ModelContextRegistry>();builder.Services.AddSingleton<HeuristicTokenCounter>();builder.Services.AddSingleton<IModelTokenizer>(_=>new TiktokenModelTokenizer(builder.Configuration["Tokenizers:TiktokenVocabPath"]));builder.Services.AddSingleton<ITokenCounter,ModelAwareTokenCounter>();builder.Services.AddScoped<IOptimizationStrategy,ExactDuplicateRemovalStrategy>();builder.Services.AddScoped<IOptimizationStrategy,RetrievalChunkDeduplicationStrategy>();builder.Services.AddScoped<IOptimizationStrategy,WhitespaceAndFormattingOptimizationStrategy>();builder.Services.AddScoped<IOptimizationStrategy,SafeToolOutputCompactionStrategy>();builder.Services.AddScoped<IOptimizationStrategy,ContextBudgetAllocationStrategy>();builder.Services.AddScoped<ProjectService>();builder.Services.AddScoped<TraceService>();builder.Services.AddScoped<ContextInspector>();
builder.Services.AddSingleton<IEvaluationStrategy,OfflineEvaluationStrategy>();
builder.Services.AddCors(o=>o.AddPolicy("local",p=>p.WithOrigins("http://localhost:5173","https://localhost:5173").AllowAnyHeader().AllowAnyMethod()));builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=1_048_576);
var app=builder.Build();
var startupLogger=app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var providerPolicy=app.Services.GetRequiredService<ProviderConnectionPolicy>();
try{providerPolicy.ValidateConfiguredBaseUrl();}catch(ProviderRequestException){throw new InvalidOperationException("Invalid provider configuration. Check Providers:OpenAICompatible:BaseUrl and AllowedHosts.");}
if(string.IsNullOrWhiteSpace(providerOptions.ApiKey))startupLogger.LogWarning("Provider API key is not configured; offline features remain available and provider calls will fail until configured.");
using(var scope=app.Services.CreateScope())
{
    var db=scope.ServiceProvider.GetRequiredService<ContextBudgetDbContext>();
    try
    {
        if(builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))await db.Database.MigrateAsync();
        else if((await db.Database.GetPendingMigrationsAsync()).Any())throw new InvalidOperationException("Pending database migrations. Apply them with 'dotnet ef database update' or explicitly enable Database:ApplyMigrationsOnStartup.");
    }
    catch(InvalidOperationException ex) when(ex.Message.StartsWith("Pending database migrations",StringComparison.Ordinal)){throw;}
    catch(Exception ex) when(ex is DbException or DbUpdateException or InvalidOperationException)
    {throw new InvalidOperationException("Database unavailable. Verify PostgreSQL is running and ConnectionStrings:Postgres points to a reachable database.");}
}
app.Use(async(ctx,next)=>{try{await next();}catch(OperationCanceledException) when(ctx.RequestAborted.IsCancellationRequested){ }catch(ProviderRequestException ex){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{code="provider_request_invalid",message=ex.Message});}catch(ArgumentException ex){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{code="validation_error",message=ex.Message});}catch(KeyNotFoundException ex){ctx.Response.StatusCode=404;await ctx.Response.WriteAsJsonAsync(new{code="not_found",message=ex.Message});}catch(JsonException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{code="invalid_json",message="JSON biçimi geçersiz."});}catch(Exception ex) when(ex is DbException or DbUpdateException){app.Logger.LogError("Database request failed with {ExceptionType} at {Path}",ex.GetType().Name,ctx.Request.Path);ctx.Response.StatusCode=503;await ctx.Response.WriteAsJsonAsync(new{code="database_unavailable",message="Veritabanı isteği tamamlayamadı; bağlantıyı kontrol edip yeniden deneyin."});}catch(Exception ex){app.Logger.LogError("API request failed with {ExceptionType} at {Path}",ex.GetType().Name,ctx.Request.Path);ctx.Response.StatusCode=500;await ctx.Response.WriteAsJsonAsync(new{code="server_error",message="İstek işlenemedi."});}});
app.UseCors("local");if(app.Environment.IsDevelopment())app.MapOpenApi();app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
var api=app.MapGroup("/api/v1");
api.MapGet("/projects",async(ProjectService svc,CancellationToken ct)=>Results.Ok(await svc.List(ct)));
api.MapPost("/projects",async(ProjectInput input,ProjectService svc,CancellationToken ct)=>Results.Created("/api/v1/projects",await svc.Create(input,ct)));
api.MapGet("/projects/{id:guid}",async(Guid id,ProjectService svc,CancellationToken ct)=>{var p=await svc.Get(id,ct);return p is null?Results.NotFound():Results.Ok(p);});
api.MapPut("/projects/{id:guid}",async(Guid id,ProjectInput input,ProjectService svc,CancellationToken ct)=>{var p=await svc.Update(id,input,ct);return p is null?Results.NotFound():Results.Ok(p);});
api.MapDelete("/projects/{id:guid}",async(Guid id,ProjectService svc,CancellationToken ct)=>await svc.Delete(id,ct)?Results.NoContent():Results.NotFound());
api.MapPost("/projects/{id:guid}/traces",async(Guid id,TraceImportDto input,TraceService svc,CancellationToken ct)=>{var error=ValidateTrace(input);if(error is not null)return Results.BadRequest(new{code="validation_error",message=error});return Results.Created($"/api/v1/projects/{id}/analyses",await svc.Import(id,input,ct));});
api.MapPost("/projects/{id:guid}/analyze",async(Guid id,AnalyzeRequest input,TraceService svc,ContextInspector inspector,ITraceRepository repo,CancellationToken ct)=>{var error=ValidateTrace(input.Trace);if(error is not null)return Results.BadRequest(new{code="validation_error",message=error});if(input.TokenBudget<0||input.OutputReserve<0)return Results.BadRequest(new{code="validation_error",message="Bütçe değerleri negatif olamaz."});if(!await repo.ProjectExists(id,ct))return Results.NotFound(new{code="not_found",message="Proje bulunamadı."});var saved=await svc.Import(id,input.Trace,ct);var messages=input.Trace.Messages!.Select(m=>new InspectionInputMessage(m.Role,m.ContentType,m.Content,m.SourceId,m.ToolCallId,m.Priority)).ToArray();var inspected=inspector.Inspect(new InspectionInput(messages,input.TokenBudget,input.OutputReserve),input.Trace.Model??"unknown");return Results.Ok(new AnalysisRunDto(saved,inspected));});
api.MapPost("/projects/{id:guid}/traces/import",async(Guid id,IFormFile file,TraceService svc,CancellationToken ct)=>{if(file.Length>1_000_000)return Results.BadRequest(new{code="file_too_large",message="Dosya en fazla 1 MB olabilir."});await using var stream=file.OpenReadStream();var input=await JsonSerializer.DeserializeAsync<TraceImportDto>(stream,new JsonSerializerOptions(JsonSerializerDefaults.Web),ct);if(input is null)return Results.BadRequest(new{code="invalid_json",message="JSON içeriği boş veya geçersiz."});var error=ValidateTrace(input);if(error is not null)return Results.BadRequest(new{code="validation_error",message=error});return Results.Ok(await svc.Import(id,input,ct));}).DisableAntiforgery();
api.MapGet("/projects/{id:guid}/analyses",async(Guid id,TraceService svc,CancellationToken ct)=>Results.Ok(await svc.History(id,ct)));
api.MapGet("/dashboard",async(ITraceRepository repo,CancellationToken ct)=>Results.Ok(await repo.Dashboard(ct)));
api.MapGet("/providers/config",(ILLMProvider provider)=>Results.Ok(provider.GetConfiguration()));
api.MapPost("/providers/test",async(ILLMProvider provider,CancellationToken ct)=>Results.Ok(await provider.TestConnectionAsync(ct)));
api.MapPost("/evaluation/offline",(EvaluationRequest input,IEvaluationStrategy evaluator)=>{if(input.OriginalContext.Length>250_000||input.CandidateContext.Length>250_000)return Results.BadRequest(new{code="validation_error",message="Bağlam başına en fazla 250.000 karakter olabilir."});return Results.Ok(evaluator.Evaluate(input));});
api.MapGet("/pricing-profiles",async(string? provider,string? model,PricingService svc,CancellationToken ct)=>Results.Ok(await svc.List(provider,model,ct)));
api.MapGet("/pricing-profiles/{id:guid}",async(Guid id,PricingService svc,CancellationToken ct)=>{var p=await svc.Get(id,ct);return p is null?Results.NotFound():Results.Ok(p);});
api.MapPost("/pricing-profiles",async(PricingProfileInput input,PricingService svc,CancellationToken ct)=>Results.Created("/api/v1/pricing-profiles",await svc.Create(input,ct)));
api.MapPost("/cost-calculations",async(CostCalculationInput input,CostAnalyticsService svc,CancellationToken ct)=>Results.Ok(await svc.Calculate(input,ct)));
api.MapGet("/experiments/runs/{runId:guid}/cost",async(Guid runId,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.Compare(runId,ct);return x is null?Results.NotFound():Results.Ok(x.Cost);});
api.MapGet("/experiments/runs/{runId:guid}/performance",async(Guid runId,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.Compare(runId,ct);return x is null?Results.NotFound():Results.Ok(x.Performance);});
api.MapGet("/experiments/runs/{runId:guid}/comparison",async(Guid runId,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.Compare(runId,ct);return x is null?Results.NotFound():Results.Ok(x);});
api.MapGet("/projects/{id:guid}/analytics",async(Guid id,CostAnalyticsService svc,CancellationToken ct)=>Results.Ok(await svc.Project(id,ct)));
api.MapGet("/experiments/{id:guid}/cost",async(Guid id,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.CompareExperiment(id,ct);return x is null?Results.NotFound():Results.Ok(x.Cost);});
api.MapGet("/experiments/{id:guid}/performance",async(Guid id,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.CompareExperiment(id,ct);return x is null?Results.NotFound():Results.Ok(x.Performance);});
api.MapGet("/experiments/{id:guid}/comparison",async(Guid id,CostAnalyticsService svc,CancellationToken ct)=>{var x=await svc.CompareExperiment(id,ct);return x is null?Results.NotFound():Results.Ok(x);});
api.MapGet("/datasets",async(Guid? projectId,ExperimentService svc,CancellationToken ct)=>Results.Ok(await svc.Datasets(projectId,ct)));
api.MapPost("/datasets",async(DatasetCreateInput input,ExperimentService svc,CancellationToken ct)=>Results.Created("/api/v1/datasets",await svc.CreateDataset(input,ct)));
api.MapPost("/datasets/{id:guid}/versions",async(Guid id,IReadOnlyList<EvaluationCaseInput> cases,ExperimentService svc,CancellationToken ct)=>Results.Created($"/api/v1/datasets/{id}/versions",await svc.CreateDatasetVersion(id,cases,ct)));
api.MapGet("/datasets/{id:guid}/versions",async(Guid id,IExperimentStore store,CancellationToken ct)=>{var d=await store.Dataset(id,ct);return d is null?Results.NotFound():Results.Ok(d.Versions.Select(v=>new DatasetVersionDto(v.Id,v.Version,v.CaseCount,v.ContentHash,v.EvaluatorVersion,v.CreatedAtUtc)));});
api.MapGet("/experiments",async(Guid? projectId,ExperimentService svc,CancellationToken ct)=>Results.Ok(await svc.Experiments(projectId,ct)));
api.MapPost("/experiments",async(ExperimentCreateInput input,ExperimentService svc,CancellationToken ct)=>Results.Created("/api/v1/experiments",await svc.CreateExperiment(input,ct)));
api.MapGet("/experiments/{id:guid}",async(Guid id,ExperimentService svc,CancellationToken ct)=>{var e=await svc.GetExperiment(id,ct);return e is null?Results.NotFound():Results.Ok(e);});
api.MapGet("/experiments/{id:guid}/runs",async(Guid id,ExperimentService svc,CancellationToken ct)=>Results.Ok(await svc.Runs(id,ct)));
api.MapPost("/experiments/{id:guid}/runs",async(Guid id,ExperimentStartInput input,ExperimentService svc,CancellationToken ct)=>{var run=await svc.Start(id,input,ct);return Results.Accepted($"/api/v1/experiments/runs/{run.Id}",run);});
api.MapGet("/experiments/runs/{runId:guid}",async(Guid runId,ExperimentService svc,CancellationToken ct)=>{var r=await svc.GetRun(runId,ct);return r is null?Results.NotFound():Results.Ok(r);});
api.MapPost("/experiments/runs/{runId:guid}/cancel",(Guid runId,ExperimentService svc)=>svc.Cancel(runId)?Results.Ok(new{cancelled=true}):Results.Conflict(new{code="not_running",message="Run is not queued or running."}));
app.Run();

static string? ValidateTrace(TraceImportDto t)
{
    if(t.Messages is null||t.Messages.Count>1000)return "messages alanı zorunludur ve en fazla 1000 mesaj içerebilir.";
    if(t.Provider?.Length>100||t.Model?.Length>200||t.TraceId?.Length>200||t.CorrelationId?.Length>200)return "Sağlayıcı, model veya iz kimliği alanı çok uzun.";
    if(t.LatencyMs<0)return "latencyMs sıfırdan küçük olamaz.";
    if(t.Usage?.InputTokens<0||t.Usage?.OutputTokens<0||t.Usage?.CachedTokens<0)return "Token kullanım değerleri negatif olamaz.";
    foreach(var m in t.Messages){if(string.IsNullOrWhiteSpace(m.Role)||m.Role.Length>40)return "Her mesajın geçerli bir role alanı olmalıdır.";if(m.Content?.Length>250_000)return "Bir mesaj 250.000 karakteri aşamaz.";if(m.ContentType?.Length>80)return "contentType alanı çok uzun.";if(m.SourceId?.Length>250||m.ToolCallId?.Length>250)return "Kaynak veya tool-call kimliği çok uzun.";if(m.Priority is <0 or >100)return "priority 0 ile 100 arasında olmalıdır.";}
    return null;
}

public partial class Program { }
