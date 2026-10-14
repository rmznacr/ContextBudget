namespace ContextBudget.Domain;

public sealed class Project
{
    private Project() { }
    public Project(string name, string? description) { Validate(name); Id=Guid.NewGuid(); Name=name.Trim(); Description=description?.Trim(); CreatedAt=UpdatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(string name,string? description) { Validate(name); Name=name.Trim(); Description=description?.Trim(); UpdatedAt=DateTimeOffset.UtcNow; }
    private static void Validate(string name) { if(string.IsNullOrWhiteSpace(name)||name.Length>120) throw new ArgumentException("Project name must be 1–120 characters.",nameof(name)); }
}

public sealed class TraceRecord
{
    private TraceRecord() { }
    public TraceRecord(Guid projectId,string? traceId,string? correlationId,string provider,string model,DateTimeOffset timestamp,string status,double? latencyMs,long? inputTokens,long? outputTokens,long? cachedTokens,string pricingSnapshotJson="null") { Id=Guid.NewGuid(); ProjectId=projectId; TraceId=traceId; CorrelationId=correlationId; Provider=provider; Model=model; Timestamp=timestamp; Status=status; LatencyMs=latencyMs; ProviderInputTokens=inputTokens; ProviderOutputTokens=outputTokens; CachedTokens=cachedTokens; PricingSnapshotJson=pricingSnapshotJson; CreatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid ProjectId { get; private set; } public Project? Project { get; private set; }
    public string? TraceId { get; private set; } public string? CorrelationId { get; private set; } public string Provider { get; private set; }="unknown"; public string Model { get; private set; }="unknown";
    public DateTimeOffset Timestamp { get; private set; } public string Status { get; private set; }="unknown"; public double? LatencyMs { get; private set; }
    public long? ProviderInputTokens { get; private set; } public long? ProviderOutputTokens { get; private set; } public long? CachedTokens { get; private set; } public string PricingSnapshotJson { get; private set; }="null"; public DateTimeOffset CreatedAt { get; private set; }
    public List<TraceSegment> Segments { get; private set; }=[];
}

public sealed class TraceSegment
{
    private TraceSegment() { }
    public TraceSegment(int ordinal,string role,string contentType,string content,long estimatedTokens,string? sourceId=null,string? toolCallId=null) { Id=Guid.NewGuid(); Ordinal=ordinal; Role=role; ContentType=contentType; SourceId=sourceId; ToolCallId=toolCallId; ContentHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))); CharacterCount=content.Length; EstimatedTokens=estimatedTokens; }
    public Guid Id { get; private set; } public Guid TraceRecordId { get; private set; } public TraceRecord? Trace { get; private set; }
    public int Ordinal { get; private set; } public string Role { get; private set; }=""; public string ContentType { get; private set; }="text"; public string? SourceId { get; private set; } public string? ToolCallId { get; private set; } public string ContentHash { get; private set; }=""; public int CharacterCount { get; private set; } public long EstimatedTokens { get; private set; }
}

public sealed class AnalysisRecord
{
    private AnalysisRecord() { }
    public AnalysisRecord(Guid projectId,Guid traceRecordId,string model,long estimatedInput,long? providerInput,long estimatedOutput,long? providerOutput) { Id=Guid.NewGuid(); ProjectId=projectId; TraceRecordId=traceRecordId; Model=model; EstimatedInputTokens=estimatedInput; ProviderInputTokens=providerInput; EstimatedOutputTokens=estimatedOutput; ProviderOutputTokens=providerOutput; CreatedAt=DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; } public Guid ProjectId { get; private set; } public Guid TraceRecordId { get; private set; } public string Model { get; private set; }=""; public long EstimatedInputTokens { get; private set; } public long? ProviderInputTokens { get; private set; } public long EstimatedOutputTokens { get; private set; } public long? ProviderOutputTokens { get; private set; } public DateTimeOffset CreatedAt { get; private set; }
}
