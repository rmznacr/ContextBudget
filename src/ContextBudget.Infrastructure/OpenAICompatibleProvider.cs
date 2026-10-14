using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextBudget.Application;

namespace ContextBudget.Infrastructure;

public sealed class LLMProviderOptions
{
    public string ProviderName { get; set; } = "EVREN Gateway";
    public string BaseUrl { get; set; } = "https://evren-llmapi.ssyz.org.tr/v1";
    public string Model { get; set; } = "deepseek-v4.1-flash";
    public string? ApiKey { get; set; }
    public string[] AllowedHosts { get; set; } = ["evren-llmapi.ssyz.org.tr"];
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxResponseBytes { get; set; } = 1_048_576;
    public int MaxConcurrentRequests { get; set; } = 2;
}

public interface IProviderDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemProviderDnsResolver : IProviderDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>Validates the configured origin and pins each socket to freshly validated public DNS answers.</summary>
public sealed class ProviderConnectionPolicy(LLMProviderOptions options, IProviderDnsResolver dns)
{
    public Uri ValidateConfiguredBaseUrl()
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Port != 443 || !IsAllowedHost(uri.Host) || !uri.AbsolutePath.TrimEnd('/').Equals("/v1", StringComparison.Ordinal))
            throw new ProviderRequestException("Provider URL policy rejected the configured address.");
        if (IPAddress.TryParse(uri.Host, out _))
            throw new ProviderRequestException("Provider URL policy requires an allowlisted DNS hostname.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public async Task<Uri> ValidateDestinationAsync(Uri uri, CancellationToken cancellationToken)
    {
        var baseUri = ValidateConfiguredBaseUrl();
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != 443 ||
            !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new ProviderRequestException("Provider destination is outside the configured HTTPS origin.");
        var addresses = await ResolvePublicAddressesAsync(uri.Host, cancellationToken);
        if (addresses.Length == 0) throw new ProviderRequestException("Provider hostname did not resolve to a permitted address.");
        return uri;
    }

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        if (!IsAllowedHost(host) || context.DnsEndPoint.Port != 443)
            throw new ProviderRequestException("Provider socket destination is outside the configured allowlist.");
        var addresses = await ResolvePublicAddressesAsync(host, cancellationToken);
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (ex is OperationCanceledException) throw;
                last = ex;
            }
        }
        throw new HttpRequestException("Could not connect to the configured provider.", last);
    }

    public SocketsHttpHandler CreateHttpHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectAsync
    };

    private async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken cancellationToken)
    {
        if (!IsAllowedHost(host) || IPAddress.TryParse(host, out _))
            throw new ProviderRequestException("Provider hostname is not allowlisted.");
        var addresses = await dns.ResolveAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new ProviderRequestException("Provider hostname resolved to a private, local, or reserved address.");
        return addresses.Distinct().ToArray();
    }

    private bool IsAllowedHost(string host) => options.AllowedHosts.Any(x => x.Equals(host, StringComparison.OrdinalIgnoreCase));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224 ||
                (b[0] == 100 && b[1] is >= 64 and <= 127) ||
                (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                (b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) ||
                (b[0] == 198 && (b[1] == 18 || b[1] == 19 || b[1] == 51)) ||
                (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        var bytes = address.GetAddressBytes();
        var globalUnicast = (bytes[0] & 0xe0) == 0x20;
        var uniqueLocal = (bytes[0] & 0xfe) == 0xfc;
        var docRange = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;
        var nat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b;
        var transitionOrSpecial = (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01) ||
            (bytes[0] == 0x20 && bytes[1] == 0x02) || (bytes[0] == 0x3f && bytes[1] == 0xff);
        return globalUnicast && !address.Equals(IPAddress.IPv6Any) && !address.Equals(IPAddress.IPv6Loopback) &&
            !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast &&
            !uniqueLocal && !docRange && !nat64 && !transitionOrSpecial;
    }
}

public sealed class OpenAICompatibleProvider(HttpClient http, LLMProviderOptions options, ProviderConnectionPolicy policy) : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly SemaphoreSlim concurrency = new(Math.Clamp(options.MaxConcurrentRequests, 1, 16));
    private readonly Uri baseUri = policy.ValidateConfiguredBaseUrl();

    public ProviderConfigurationDto GetConfiguration() => new(options.ProviderName, baseUri.ToString(), options.Model,
        !string.IsNullOrWhiteSpace(options.ApiKey), !string.IsNullOrWhiteSpace(options.ApiKey));

    public Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken) => TestCoreAsync(cancellationToken);

    private async Task<ProviderTestResult> TestCoreAsync(CancellationToken cancellationToken)
    {
        var response = await CompleteAsync(new LLMRequest(options.Model, [new("user", "Reply with OK.")], null, 8), cancellationToken);
        return new(response.Provider, response.Model, response.DurationMs, response.Usage, response.Status, response.Error);
    }

    public async Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? options.Model : request.Model.Trim();
        ValidateRequest(request, model);
        var timer = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return Failure(model, timer.ElapsedMilliseconds, "MissingApiKey", "Provider API key is not configured.");
        var uri = new Uri(baseUri, "chat/completions");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120)));
        var requestToken = timeout.Token;
        try
        {
            await policy.ValidateDestinationAsync(uri, requestToken);
            await concurrency.WaitAsync(requestToken);
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, uri);
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
                message.Content = JsonContent.Create(new
                {
                    model,
                    messages = request.Messages.Select(x => new ProviderMessage(x.Role, x.Content, x.ToolCallId)).ToArray(),
                    temperature = request.Temperature,
                    max_tokens = request.MaxOutputTokens
                }, options: JsonOptions);
                using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken);
                if (!response.IsSuccessStatusCode)
                {
                    var (category, errorMessage) = await MapErrorAsync(response, Math.Clamp(options.MaxResponseBytes, 1, 10_485_760), requestToken);
                    timer.Stop();
                    return Failure(model, timer.ElapsedMilliseconds, category, errorMessage);
                }
                var body = await ReadBoundedAsync(response.Content, Math.Clamp(options.MaxResponseBytes, 1, 10_485_760), requestToken);
                timer.Stop();
                return ParseResponse(body, timer.ElapsedMilliseconds, model);
            }
            finally { concurrency.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "Timeout", "Provider request timed out."); }
        catch (ProviderRequestException ex) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "ConnectionError", ex.Message); }
        catch (ResponseTooLargeException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "InvalidResponse", "Provider response exceeded the configured size limit."); }
        catch (HttpRequestException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "ConnectionError", "Could not connect to the configured provider."); }
        catch (SocketException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "ConnectionError", "Could not resolve or connect to the configured provider."); }
        catch (KeyNotFoundException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "InvalidResponse", "Provider returned malformed JSON or an invalid response shape."); }
        catch (InvalidOperationException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "InvalidResponse", "Provider returned malformed JSON or an invalid response shape."); }
        catch (JsonException) { timer.Stop(); return Failure(model, timer.ElapsedMilliseconds, "InvalidResponse", "Provider returned malformed JSON or an invalid response shape."); }
    }

    private LLMResponse ParseResponse(byte[] bytes, long duration, string requestedModel)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var choice = root.GetProperty("choices").EnumerateArray().FirstOrDefault();
        if (choice.ValueKind != JsonValueKind.Object) throw new JsonException();
        var message = choice.GetProperty("message");
        var contentElement = message.GetProperty("content");
        if (contentElement.ValueKind != JsonValueKind.String) throw new JsonException();
        var model = root.TryGetProperty("model", out var modelValue) && modelValue.ValueKind == JsonValueKind.String ? modelValue.GetString() : null;
        string? finish = choice.TryGetProperty("finish_reason", out var finishValue) && finishValue.ValueKind == JsonValueKind.String ? finishValue.GetString() : null;
        ProviderUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageValue) && usageValue.ValueKind == JsonValueKind.Object)
        {
            long? input = ReadLong(usageValue, "prompt_tokens");
            long? output = ReadLong(usageValue, "completion_tokens");
            long? cached = null;
            if (usageValue.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
                cached = ReadLong(details, "cached_tokens");
            usage = new(input, output, cached);
        }
        return new(options.ProviderName, Redact(model ?? requestedModel), Redact(contentElement.GetString()), usage, finish, duration, "SUCCESS");
    }

    private static long? ReadLong(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    private string? Redact(string? value) => string.IsNullOrEmpty(value) || string.IsNullOrEmpty(options.ApiKey)
        ? value
        : value.Replace(options.ApiKey, "[REDACTED]", StringComparison.Ordinal);

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > 0 and var length && length > maxBytes) throw new ResponseTooLargeException();
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(Math.Min(maxBytes, 16_384));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (output.Length + read > maxBytes) throw new ResponseTooLargeException();
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ValidateRequest(LLMRequest request, string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200) throw new ProviderRequestException("A valid model name is required.");
        if (request.Messages is null || request.Messages.Count is < 1 or > 1000) throw new ProviderRequestException("Between 1 and 1000 messages are required.");
        foreach (var message in request.Messages)
        {
            if (message.Role is not ("system" or "developer" or "user" or "assistant" or "tool"))
                throw new ProviderRequestException("Unsupported provider message role.");
            if (message.Content is null || message.Content.Length > 250_000) throw new ProviderRequestException("Message content is missing or too long.");
            if (message.Role == "tool" && string.IsNullOrWhiteSpace(message.ToolCallId)) throw new ProviderRequestException("Tool messages require a toolCallId.");
        }
        if (request.Temperature is < 0 or > 2) throw new ProviderRequestException("Temperature must be between 0 and 2.");
        if (request.MaxOutputTokens is < 1 or > 200_000) throw new ProviderRequestException("maxOutputTokens must be between 1 and 200000.");
    }

    private static string MapStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "AuthenticationError",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.TooManyRequests => "RateLimit",
        HttpStatusCode.NotFound => "InvalidModel",
        _ => "ConnectionError"
    };
    private static async Task<(string Category, string Message)> MapErrorAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
        {
            try
            {
                var bytes = await ReadBoundedAsync(response.Content, maxBytes, ct);
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String &&
                    code.GetString() is "invalid_model" or "model_not_found")
                    return ("InvalidModel", "The configured model was not recognized by the provider.");
            }
            catch (Exception ex) when (ex is JsonException or ResponseTooLargeException) { }
        }
        return (MapStatus(response.StatusCode), SafeStatusMessage(response.StatusCode));
    }
    private static string SafeStatusMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Provider rejected the configured API key.",
        HttpStatusCode.Forbidden => "Provider denied access to this model or account.",
        HttpStatusCode.TooManyRequests => "Provider rate limit was reached.",
        HttpStatusCode.NotFound => "Provider endpoint or model was not found.",
        _ => $"Provider returned HTTP {(int)status}."
    };
    private LLMResponse Failure(string model, long duration, string category, string message) => new(options.ProviderName, model, null, null, null, duration, "ERROR", new(category, message));
    private sealed record ProviderMessage([property: JsonPropertyName("role")] string Role, [property: JsonPropertyName("content")] string Content, [property: JsonPropertyName("tool_call_id")] string? ToolCallId);
    private sealed class ResponseTooLargeException : Exception;
}
