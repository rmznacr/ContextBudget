using ContextBudget.Application;
using ContextBudget.Domain;
using Microsoft.EntityFrameworkCore;
namespace ContextBudget.Infrastructure;
public sealed class ContextBudgetDbContext(DbContextOptions<ContextBudgetDbContext> options):DbContext(options)
{
    public DbSet<Project> Projects=>Set<Project>(); public DbSet<TraceRecord> Traces=>Set<TraceRecord>(); public DbSet<TraceSegment> TraceSegments=>Set<TraceSegment>(); public DbSet<AnalysisRecord> Analyses=>Set<AnalysisRecord>();
    public DbSet<PricingProfile> PricingProfiles=>Set<PricingProfile>(); public DbSet<EvaluationDataset> EvaluationDatasets=>Set<EvaluationDataset>(); public DbSet<EvaluationDatasetVersion> DatasetVersions=>Set<EvaluationDatasetVersion>(); public DbSet<Experiment> Experiments=>Set<Experiment>(); public DbSet<ExperimentRun> ExperimentRuns=>Set<ExperimentRun>(); public DbSet<ExperimentCaseResult> ExperimentCaseResults=>Set<ExperimentCaseResult>();
    protected override void OnModelCreating(ModelBuilder b){b.Entity<Project>().HasKey(x=>x.Id);b.Entity<Project>().Property(x=>x.Name).HasMaxLength(120).IsRequired();b.Entity<Project>().HasMany<TraceRecord>().WithOne(x=>x.Project).HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Cascade);b.Entity<Project>().HasMany<AnalysisRecord>().WithOne().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Cascade);b.Entity<TraceRecord>().HasKey(x=>x.Id);b.Entity<TraceRecord>().HasIndex(x=>new{x.ProjectId,x.Timestamp});b.Entity<TraceRecord>().HasIndex(x=>x.CorrelationId);b.Entity<TraceRecord>().Property(x=>x.PricingSnapshotJson).HasColumnType("jsonb").HasDefaultValueSql("'null'::jsonb");b.Entity<TraceRecord>().HasMany(x=>x.Segments).WithOne(x=>x.Trace).HasForeignKey(x=>x.TraceRecordId).OnDelete(DeleteBehavior.Cascade);b.Entity<TraceRecord>().HasMany<AnalysisRecord>().WithOne().HasForeignKey(x=>x.TraceRecordId).OnDelete(DeleteBehavior.Cascade);b.Entity<TraceSegment>().HasKey(x=>x.Id);b.Entity<TraceSegment>().Property(x=>x.SourceId).HasMaxLength(250);b.Entity<TraceSegment>().Property(x=>x.ToolCallId).HasMaxLength(250);b.Entity<AnalysisRecord>().HasKey(x=>x.Id);b.Entity<AnalysisRecord>().HasIndex(x=>new{x.ProjectId,x.CreatedAt});
    b.Entity<EvaluationDataset>().HasKey(x=>x.Id); b.Entity<EvaluationDataset>().Property(x=>x.Name).HasMaxLength(120).IsRequired(); b.Entity<EvaluationDataset>().HasMany(x=>x.Versions).WithOne(x=>x.Dataset).HasForeignKey(x=>x.DatasetId).OnDelete(DeleteBehavior.Cascade); b.Entity<EvaluationDataset>().HasOne<Project>().WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Cascade);
    b.Entity<EvaluationDatasetVersion>().HasKey(x=>x.Id); b.Entity<EvaluationDatasetVersion>().HasIndex(x=>new{x.DatasetId,x.Version}).IsUnique(); b.Entity<EvaluationDatasetVersion>().Property(x=>x.ContentHash).HasMaxLength(64).IsRequired(); b.Entity<EvaluationDatasetVersion>().Property(x=>x.EvaluatorVersion).HasMaxLength(40).IsRequired();
    b.Entity<Experiment>().HasKey(x=>x.Id); b.Entity<Experiment>().HasIndex(x=>new{x.ProjectId,x.CreatedAtUtc}); b.Entity<Experiment>().HasOne<Project>().WithMany().HasForeignKey(x=>x.ProjectId).OnDelete(DeleteBehavior.Cascade); b.Entity<Experiment>().HasOne(x=>x.DatasetVersion).WithMany().HasForeignKey(x=>x.DatasetVersionId).OnDelete(DeleteBehavior.Cascade); b.Entity<Experiment>().Property(x=>x.Name).HasMaxLength(120); b.Entity<Experiment>().Property(x=>x.Provider).HasMaxLength(80); b.Entity<Experiment>().Property(x=>x.Model).HasMaxLength(200); b.Entity<Experiment>().Property(x=>x.BaselineContextHash).HasMaxLength(64); b.Entity<Experiment>().Property(x=>x.OptimizedContextHash).HasMaxLength(64);
    b.Entity<ExperimentRun>().HasKey(x=>x.Id); b.Entity<ExperimentRun>().HasIndex(x=>new{x.ExperimentId,x.IdempotencyKey}).IsUnique(); b.Entity<ExperimentRun>().HasIndex(x=>new{x.ExperimentId,x.Status}); b.Entity<ExperimentRun>().Property(x=>x.IdempotencyKey).HasMaxLength(100); b.Entity<ExperimentRun>().Property(x=>x.SummaryJson).HasColumnType("jsonb"); b.Entity<ExperimentRun>().Property(x=>x.PricingSnapshotJson).HasColumnType("jsonb"); b.Entity<ExperimentRun>().HasOne(x=>x.Experiment).WithMany(x=>x.Runs).HasForeignKey(x=>x.ExperimentId).OnDelete(DeleteBehavior.Cascade); b.Entity<ExperimentRun>().HasMany(x=>x.Results).WithOne(x=>x.Run).HasForeignKey(x=>x.RunId).OnDelete(DeleteBehavior.Cascade);
    b.Entity<PricingProfile>().HasKey(x=>x.Id); b.Entity<PricingProfile>().HasIndex(x=>new{x.Provider,x.ModelId,x.PricingMode,x.EffectiveFromUtc}); b.Entity<PricingProfile>().Property(x=>x.InputPricePerMillionTokens).HasPrecision(24,12); b.Entity<PricingProfile>().Property(x=>x.OutputPricePerMillionTokens).HasPrecision(24,12); b.Entity<PricingProfile>().Property(x=>x.CachedInputPricePerMillionTokens).HasPrecision(24,12); b.Entity<PricingProfile>().Property(x=>x.CacheWritePricePerMillionTokens).HasPrecision(24,12); b.Entity<PricingProfile>().Property(x=>x.Provider).HasMaxLength(80); b.Entity<PricingProfile>().Property(x=>x.ModelId).HasMaxLength(200); b.Entity<PricingProfile>().Property(x=>x.Currency).HasMaxLength(3); b.Entity<PricingProfile>().Property(x=>x.PricingMode).HasMaxLength(80); b.Entity<PricingProfile>().Property(x=>x.SourceDescription).HasMaxLength(500); b.Entity<ExperimentCaseResult>().HasKey(x=>x.Id); b.Entity<ExperimentCaseResult>().HasIndex(x=>new{x.RunId,x.CaseId,x.Repeat,x.Variant}).IsUnique(); b.Entity<ExperimentCaseResult>().Property(x=>x.ChecksJson).HasColumnType("jsonb"); b.Entity<ExperimentCaseResult>().Property(x=>x.CaseId).HasMaxLength(80); b.Entity<ExperimentCaseResult>().Property(x=>x.CaseName).HasMaxLength(160); b.Entity<ExperimentCaseResult>().Property(x=>x.Variant).HasMaxLength(1); b.Entity<ExperimentCaseResult>().Property(x=>x.FailureCode).HasMaxLength(80);
    }
}

public sealed class ExperimentStore(ContextBudgetDbContext db):IExperimentStore
{
    public Task<PricingProfile?> ActivePricing(string provider,string model,DateTimeOffset at,CancellationToken ct)=>db.PricingProfiles.AsNoTracking().Where(x=>x.Provider==provider&&x.ModelId==model&&x.EffectiveFromUtc<=at&&(x.EffectiveToUtc==null||x.EffectiveToUtc>at)).OrderByDescending(x=>x.EffectiveFromUtc).FirstOrDefaultAsync(ct);
    public Task<bool> ProjectExists(Guid id,CancellationToken ct)=>db.Projects.AnyAsync(x=>x.Id==id,ct);
    public Task<EvaluationDataset?> Dataset(Guid id,CancellationToken ct)=>db.EvaluationDatasets.Include(x=>x.Versions).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public Task<EvaluationDatasetVersion?> DatasetVersion(Guid id,CancellationToken ct)=>db.DatasetVersions.Include(x=>x.Dataset).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task AddDataset(EvaluationDataset d,EvaluationDatasetVersion v,CancellationToken ct){await db.EvaluationDatasets.AddAsync(d,ct);await db.SaveChangesAsync(ct);}
    public async Task AddDatasetVersion(EvaluationDatasetVersion version,CancellationToken ct){await db.DatasetVersions.AddAsync(version,ct);await db.SaveChangesAsync(ct);}
    public async Task<IReadOnlyList<EvaluationDataset>> Datasets(Guid? projectId,CancellationToken ct)=>await db.EvaluationDatasets.AsNoTracking().Include(x=>x.Versions).Where(x=>projectId==null||x.ProjectId==projectId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
    public async Task AddExperiment(Experiment e,CancellationToken ct){await db.Experiments.AddAsync(e,ct);await db.SaveChangesAsync(ct);}
    public async Task<IReadOnlyList<Experiment>> Experiments(Guid? projectId,CancellationToken ct)=>await db.Experiments.AsNoTracking().Include(x=>x.DatasetVersion).Include(x=>x.Runs).Where(x=>projectId==null||x.ProjectId==projectId).OrderByDescending(x=>x.CreatedAtUtc).ToListAsync(ct);
    public Task<Experiment?> Experiment(Guid id,CancellationToken ct)=>db.Experiments.Include(x=>x.DatasetVersion).Include(x=>x.Runs).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public Task<ExperimentRun?> Run(Guid id,CancellationToken ct)=>db.ExperimentRuns.Include(x=>x.Experiment!).ThenInclude(x=>x.DatasetVersion).Include(x=>x.Results).FirstOrDefaultAsync(x=>x.Id==id,ct);
    public Task<ExperimentRun?> RunByKey(Guid id,string key,CancellationToken ct)=>db.ExperimentRuns.Include(x=>x.Experiment!).ThenInclude(x=>x.DatasetVersion).Include(x=>x.Results).FirstOrDefaultAsync(x=>x.ExperimentId==id&&x.IdempotencyKey==key,ct);
    public Task<ExperimentRun?> ActiveRun(Guid id,CancellationToken ct)=>db.ExperimentRuns.Include(x=>x.Experiment!).ThenInclude(x=>x.DatasetVersion).Include(x=>x.Results).FirstOrDefaultAsync(x=>x.ExperimentId==id&&(x.Status=="Pending"||x.Status=="Running"),ct);
    public async Task<IReadOnlyList<ExperimentRun>> Runs(Guid id,CancellationToken ct)=>await db.ExperimentRuns.AsNoTracking().Include(x=>x.Experiment!).ThenInclude(x=>x.DatasetVersion).Include(x=>x.Results).Where(x=>x.ExperimentId==id).OrderByDescending(x=>x.CreatedAtUtc).Take(50).ToListAsync(ct);
    public async Task AddRun(ExperimentRun r,CancellationToken ct){await db.ExperimentRuns.AddAsync(r,ct);await db.SaveChangesAsync(ct);}
    public async Task SaveRun(ExperimentRun r,IReadOnlyList<ExperimentCaseResult> results,CancellationToken ct){await db.ExperimentCaseResults.AddRangeAsync(results,ct);await db.SaveChangesAsync(ct);}
    public async Task SaveRunState(ExperimentRun r,Experiment e,string status,string gate,string summary,CancellationToken ct){r.Update(status,gate,summary);e.SetStatus(status);await db.SaveChangesAsync(ct);}
}
public sealed class ProjectRepository(ContextBudgetDbContext db):IProjectRepository
{
    public async Task<IReadOnlyList<Project>> List(CancellationToken ct)=>await db.Projects.AsNoTracking().OrderByDescending(x=>x.CreatedAt).ToListAsync(ct);
    public Task<Project?> Get(Guid id,CancellationToken ct)=>db.Projects.FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task Add(Project p,CancellationToken ct)=>await db.Projects.AddAsync(p,ct);
    public Task Save(CancellationToken ct)=>db.SaveChangesAsync(ct);
    public Task Delete(Project p,CancellationToken ct){db.Projects.Remove(p);return Task.CompletedTask;}
}
public sealed class TraceRepository(ContextBudgetDbContext db):ITraceRepository
{
    public Task<bool> ProjectExists(Guid id,CancellationToken ct)=>db.Projects.AnyAsync(x=>x.Id==id,ct);
    public async Task Add(TraceRecord t,AnalysisRecord a,CancellationToken ct){await db.Traces.AddAsync(t,ct);await db.Analyses.AddAsync(a,ct);await db.SaveChangesAsync(ct);}
    public async Task<IReadOnlyList<AnalysisRecord>> Analyses(Guid id,CancellationToken ct)=>await db.Analyses.AsNoTracking().Where(x=>x.ProjectId==id).OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync(ct);
    public async Task<IReadOnlyList<TraceRecord>> ProjectTracesForCostAnalytics(Guid projectId,CancellationToken ct)=>await db.Traces.AsNoTracking().Where(x=>x.ProjectId==projectId).OrderByDescending(x=>x.Timestamp).Take(1000).ToListAsync(ct);
    public async Task<DashboardDto> Dashboard(CancellationToken ct){var ts=db.Traces.AsNoTracking();var aa=db.Analyses.AsNoTracking();var total=await aa.SumAsync(x=>(long?)x.EstimatedInputTokens,ct);var prov=await aa.SumAsync(x=>(long?)x.ProviderInputTokens,ct);var recent=await ts.OrderByDescending(x=>x.Timestamp).Take(8).Select(x=>new RecentTraceDto(x.Id,x.ProjectId,x.Provider,x.Model,x.Timestamp,x.ProviderInputTokens,null)).ToListAsync(ct);return new(await db.Projects.CountAsync(ct),await ts.CountAsync(ct),total,prov,recent);}
}

public sealed class PricingStore(ContextBudgetDbContext db):IPricingStore
{
    public Task<PricingProfile?> Get(Guid id,CancellationToken ct)=>db.PricingProfiles.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task<IReadOnlyList<PricingProfile>> List(string? provider,string? model,CancellationToken ct)=>await db.PricingProfiles.AsNoTracking().Where(x=>(provider==null||x.Provider==provider)&&(model==null||x.ModelId==model)).OrderBy(x=>x.Provider).ThenBy(x=>x.ModelId).ThenByDescending(x=>x.EffectiveFromUtc).ToListAsync(ct);
    public Task<PricingProfile?> FindActive(string provider,string model,DateTimeOffset at,CancellationToken ct)=>db.PricingProfiles.AsNoTracking().Where(x=>x.Provider==provider&&x.ModelId==model&&x.EffectiveFromUtc<=at&&(x.EffectiveToUtc==null||x.EffectiveToUtc>at)).OrderByDescending(x=>x.EffectiveFromUtc).FirstOrDefaultAsync(ct);
    public async Task CreateVersion(PricingProfile profile,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var same=await db.PricingProfiles.Where(x=>x.Provider==profile.Provider&&x.ModelId==profile.ModelId&&x.PricingMode==profile.PricingMode).OrderByDescending(x=>x.EffectiveFromUtc).ToListAsync(ct);
        var conflict=same.FirstOrDefault(x=>profile.EffectiveFromUtc<x.EffectiveToUtc.GetValueOrDefault(DateTimeOffset.MaxValue)&&x.EffectiveFromUtc<profile.EffectiveToUtc.GetValueOrDefault(DateTimeOffset.MaxValue));
        if(conflict is not null)
        {
            if(conflict.EffectiveToUtc is null&&profile.EffectiveFromUtc>conflict.EffectiveFromUtc&&profile.EffectiveFromUtc<conflict.EffectiveToUtc.GetValueOrDefault(DateTimeOffset.MaxValue)) conflict.CloseAt(profile.EffectiveFromUtc);
            else throw new ArgumentException("Pricing effective periods cannot overlap for the same provider, model, and mode.");
        }
        await db.PricingProfiles.AddAsync(profile,ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<ExperimentRun>> ProjectRuns(Guid projectId,CancellationToken ct)=>await db.ExperimentRuns.AsNoTracking().Include(x=>x.Experiment).Include(x=>x.Results).Where(x=>x.Experiment!=null&&x.Experiment.ProjectId==projectId).OrderBy(x=>x.CreatedAtUtc).Take(500).ToListAsync(ct);
}
