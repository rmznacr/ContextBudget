namespace ContextBudget.Application;

public sealed record LLMMessage(string Role, string Content, string? ToolCallId = null);
public sealed record LLMRequest(string? Model, IReadOnlyList<LLMMessage> Messages, double? Temperature = null, int? MaxOutputTokens = null);
public sealed record ProviderUsage(long? InputTokens, long? OutputTokens, long? CachedTokens);
public sealed record ProviderError(string Category, string Message);
public sealed record LLMResponse(string Provider, string? Model, string? Content, ProviderUsage? Usage, string? FinishReason, long DurationMs, string Status, ProviderError? Error = null);
public sealed record ProviderConfigurationDto(string Provider, string BaseUrl, string Model, bool ApiKeyConfigured, bool Enabled);
public sealed record ProviderTestResult(string Provider, string? Model, long DurationMs, ProviderUsage? Usage, string Status, ProviderError? Error = null);

public interface ILLMProvider
{
    ProviderConfigurationDto GetConfiguration();
    Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken);
    Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}

public sealed class ProviderRequestException(string message) : Exception(message);
